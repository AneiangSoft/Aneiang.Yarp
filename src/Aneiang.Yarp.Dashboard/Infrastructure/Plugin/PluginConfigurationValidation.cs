using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>Validates plugin configuration instances against manifest-provided JSON schemas.</summary>
public interface IPluginConfigurationSchemaValidator
{
    bool TryValidate(string configJson, string schemaJson, out string normalizedJson, out string error);
}

public sealed class PluginConfigurationSchemaValidator : IPluginConfigurationSchemaValidator
{
    public bool TryValidate(string configJson, string schemaJson, out string normalizedJson, out string error)
    {
        normalizedJson = string.Empty;
        error = string.Empty;

        try
        {
            using var instance = JsonDocument.Parse(configJson);
            using var schema = JsonDocument.Parse(schemaJson);
            if (schema.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "The plugin configuration schema must be a JSON object.";
                return false;
            }

            if (!Validate(instance.RootElement, schema.RootElement, "$", out error))
                return false;

            normalizedJson = instance.RootElement.GetRawText();
            return true;
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool Validate(JsonElement value, JsonElement schema, string path, out string error)
    {
        error = string.Empty;
        if (schema.TryGetProperty("allOf", out var allOf) && allOf.ValueKind == JsonValueKind.Array)
        {
            foreach (var branch in allOf.EnumerateArray())
            {
                if (branch.ValueKind == JsonValueKind.Object && !Validate(value, branch, path, out error)) return false;
            }
        }
        if (schema.TryGetProperty("oneOf", out var oneOf) && oneOf.ValueKind == JsonValueKind.Array)
        {
            var matches = oneOf.EnumerateArray().Count(branch => branch.ValueKind == JsonValueKind.Object && Validate(value, branch, path, out _));
            if (matches != 1)
            {
                error = $"{path} must match exactly one oneOf schema (matched {matches}).";
                return false;
            }
        }
        if (schema.TryGetProperty("anyOf", out var anyOf) && anyOf.ValueKind == JsonValueKind.Array)
        {
            if (!anyOf.EnumerateArray().Any(branch => branch.ValueKind == JsonValueKind.Object && Validate(value, branch, path, out _)))
            {
                error = $"{path} must match at least one anyOf schema.";
                return false;
            }
        }
        if (schema.TryGetProperty("const", out var constant) && !JsonEquals(value, constant))
        {
            error = $"{path} must equal the declared constant.";
            return false;
        }
        if (schema.TryGetProperty("type", out var type) && !MatchesType(value, type))
        {
            error = $"{path} does not match the declared type '{TypeDescription(type)}'.";
            return false;
        }

        if (schema.TryGetProperty("enum", out var enumValues) && enumValues.ValueKind == JsonValueKind.Array &&
            !enumValues.EnumerateArray().Any(candidate => JsonEquals(value, candidate)))
        {
            error = $"{path} is not one of the allowed enum values.";
            return false;
        }

        if (value.ValueKind == JsonValueKind.Object && !ValidateObject(value, schema, path, out error))
            return false;
        if (value.ValueKind == JsonValueKind.Array && !ValidateArray(value, schema, path, out error))
            return false;
        if (value.ValueKind == JsonValueKind.String && !ValidateString(value, schema, path, out error))
            return false;
        if (value.ValueKind == JsonValueKind.Number && !ValidateNumber(value, schema, path, out error))
            return false;

        return true;
    }

    private static bool ValidateObject(JsonElement value, JsonElement schema, string path, out string error)
    {
        error = string.Empty;
        var properties = schema.TryGetProperty("properties", out var declared) && declared.ValueKind == JsonValueKind.Object
            ? declared
            : default;

        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !value.TryGetProperty(item.GetString()!, out _))
                {
                    error = $"{AppendProperty(path, item.GetString()!)} is required.";
                    return false;
                }
            }
        }

        if (schema.TryGetProperty("minProperties", out var minProperties) && minProperties.TryGetInt32(out var minimumProperties) && value.EnumerateObject().Count() < minimumProperties)
        {
            error = $"{path} must contain at least {minimumProperties} properties.";
            return false;
        }
        if (schema.TryGetProperty("maxProperties", out var maxProperties) && maxProperties.TryGetInt32(out var maximumProperties) && value.EnumerateObject().Count() > maximumProperties)
        {
            error = $"{path} must contain no more than {maximumProperties} properties.";
            return false;
        }

        foreach (var property in value.EnumerateObject())
        {
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var propertySchema))
            {
                if (!Validate(property.Value, propertySchema, AppendProperty(path, property.Name), out error))
                    return false;
            }
            else if (schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False)
            {
                error = $"{path} contains undeclared property '{property.Name}'.";
                return false;
            }
            else if (schema.TryGetProperty("additionalProperties", out additional) && additional.ValueKind == JsonValueKind.Object &&
                     !Validate(property.Value, additional, AppendProperty(path, property.Name), out error))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateArray(JsonElement value, JsonElement schema, string path, out string error)
    {
        error = string.Empty;
        var items = value.EnumerateArray().ToArray();
        if (schema.TryGetProperty("minItems", out var minItems) && minItems.TryGetInt32(out var minimumItems) && items.Length < minimumItems)
        {
            error = $"{path} must contain at least {minimumItems} items.";
            return false;
        }
        if (schema.TryGetProperty("maxItems", out var maxItems) && maxItems.TryGetInt32(out var maximumItems) && items.Length > maximumItems)
        {
            error = $"{path} must contain no more than {maximumItems} items.";
            return false;
        }
        if (schema.TryGetProperty("uniqueItems", out var unique) && unique.ValueKind == JsonValueKind.True)
        {
            for (var i = 0; i < items.Length; i++)
            {
                for (var j = i + 1; j < items.Length; j++)
                {
                    if (JsonEquals(items[i], items[j]))
                    {
                        error = $"{path} must contain unique items.";
                        return false;
                    }
                }
            }
        }

        if (schema.TryGetProperty("items", out var itemSchema) && itemSchema.ValueKind == JsonValueKind.Object)
        {
            for (var i = 0; i < items.Length; i++)
            {
                if (!Validate(items[i], itemSchema, $"{path}[{i}]", out error))
                    return false;
            }
        }

        return true;
    }

    private static bool ValidateString(JsonElement value, JsonElement schema, string path, out string error)
    {
        error = string.Empty;
        var text = value.GetString() ?? string.Empty;
        if (schema.TryGetProperty("minLength", out var minLength) && minLength.TryGetInt32(out var minimum) && text.Length < minimum)
        {
            error = $"{path} must contain at least {minimum} characters.";
            return false;
        }

        if (schema.TryGetProperty("maxLength", out var maxLength) && maxLength.TryGetInt32(out var maximum) && text.Length > maximum)
        {
            error = $"{path} must contain no more than {maximum} characters.";
            return false;
        }
        if (schema.TryGetProperty("pattern", out var pattern) && pattern.ValueKind == JsonValueKind.String)
        {
            try
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(text, pattern.GetString()!))
                {
                    error = $"{path} does not match the required pattern.";
                    return false;
                }
            }
            catch (ArgumentException)
            {
                error = $"{path} has an invalid schema pattern.";
                return false;
            }
        }
        if (schema.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String &&
            string.Equals(format.GetString(), "duration", StringComparison.OrdinalIgnoreCase) &&
            (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var duration) || duration < TimeSpan.Zero))
        {
            error = $"{path} must be a non-negative duration.";
            return false;
        }

        return true;
    }

    private static bool ValidateNumber(JsonElement value, JsonElement schema, string path, out string error)
    {
        error = string.Empty;
        if (!value.TryGetDecimal(out var number))
        {
            error = $"{path} is outside the supported numeric range.";
            return false;
        }

        if (schema.TryGetProperty("minimum", out var minimumElement) && minimumElement.TryGetDecimal(out var minimum) && number < minimum)
        {
            error = $"{path} must be greater than or equal to {minimum.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }
        if (schema.TryGetProperty("maximum", out var maximumElement) && maximumElement.TryGetDecimal(out var maximum) && number > maximum)
        {
            error = $"{path} must be less than or equal to {maximum.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        return true;
    }

    private static bool MatchesType(JsonElement value, JsonElement type)
    {
        if (type.ValueKind == JsonValueKind.String)
            return MatchesType(value, type.GetString());
        return type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(candidate =>
            candidate.ValueKind == JsonValueKind.String && MatchesType(value, candidate.GetString()));
    }

    private static bool MatchesType(JsonElement value, string? type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false
    };

    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;

        return left.ValueKind switch
        {
            JsonValueKind.Object => ObjectEquals(left, right),
            JsonValueKind.Array => left.EnumerateArray().SequenceEqual(right.EnumerateArray(), JsonElementComparer.Instance),
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText() ||
                                    left.TryGetDecimal(out var leftNumber) && right.TryGetDecimal(out var rightNumber) && leftNumber == rightNumber,
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => left.GetRawText() == right.GetRawText()
        };
    }

    private static bool ObjectEquals(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        var rightProperties = right.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        return leftProperties.Count == rightProperties.Count && leftProperties.All(property =>
            rightProperties.TryGetValue(property.Key, out var rightValue) && JsonEquals(property.Value, rightValue));
    }

    private sealed class JsonElementComparer : IEqualityComparer<JsonElement>
    {
        public static readonly JsonElementComparer Instance = new();
        public bool Equals(JsonElement x, JsonElement y) => JsonEquals(x, y);
        public int GetHashCode(JsonElement obj) => obj.GetRawText().GetHashCode(StringComparison.Ordinal);
    }

    private static string TypeDescription(JsonElement type) => type.ValueKind == JsonValueKind.String
        ? type.GetString() ?? string.Empty
        : type.GetRawText();

    private static string AppendProperty(string path, string propertyName)
    {
        if (propertyName.All(character => char.IsLetterOrDigit(character) || character == '_'))
            return $"{path}.{propertyName}";
        return $"{path}['{propertyName.Replace("'", "\\'", StringComparison.Ordinal)}']";
    }
}

/// <summary>Service that coordinates plugin configuration migrations.</summary>
public sealed class PluginConfigurationMigrationService : IPluginConfigurationMigrationService
{
    private readonly IReadOnlyDictionary<(string PluginId, int FromVersion, int ToVersion), IPluginConfigurationMigrator> _migrators;

    public PluginConfigurationMigrationService(IEnumerable<IPluginConfigurationMigrator> migrators)
    {
        _migrators = migrators.ToDictionary(
            migrator => (migrator.PluginId.ToUpperInvariant(), migrator.FromVersion, migrator.ToVersion));
    }

    public bool TryMigrate(string pluginId, int fromVersion, int toVersion, string configJson, out string migratedConfigJson, out string error)
    {
        migratedConfigJson = configJson;
        error = string.Empty;
        if (fromVersion == toVersion)
            return true;

        var direction = Math.Sign(toVersion - fromVersion);
        var currentVersion = fromVersion;
        while (currentVersion != toVersion)
        {
            var nextVersion = currentVersion + direction;
            if (!_migrators.TryGetValue((pluginId.ToUpperInvariant(), currentVersion, nextVersion), out var migrator))
            {
                error = $"No configuration migrator is registered for plugin '{pluginId}' from schema v{currentVersion} to v{nextVersion}.";
                migratedConfigJson = configJson;
                return false;
            }

            if (!migrator.TryMigrate(migratedConfigJson, out var nextJson, out error))
            {
                migratedConfigJson = configJson;
                error = $"Configuration migration from schema v{currentVersion} to v{nextVersion} failed: {error}";
                return false;
            }

            migratedConfigJson = nextJson;
            currentVersion = nextVersion;
        }

        return true;
    }
}
