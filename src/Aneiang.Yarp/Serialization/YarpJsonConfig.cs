using System.Text.Json;
using System.Text.Json.Serialization;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Serialization;

/// <summary>
/// Single source of truth for serializing and deserializing native YARP
/// <see cref="RouteConfig"/> and <see cref="ClusterConfig"/> objects across the whole stack
/// (persistence, API parsing, import/export).
/// Output keys are PascalCase to match the canonical YARP appsettings layout, while reading
/// is case-insensitive and tolerant of JSON comments and trailing commas.
/// </summary>
public static class YarpJsonConfig
{
    /// <summary>
    /// Lenient options used everywhere: PascalCase output, case-insensitive input,
    /// comments skipped, trailing commas allowed, enums as strings, nulls omitted.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = BuildOptions(writeIndented: false);

    /// <summary>Same as <see cref="Options"/> but with indentation for human-friendly output.</summary>
    public static JsonSerializerOptions IndentedOptions { get; } = BuildOptions(writeIndented: true);

    private static JsonSerializerOptions BuildOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = writeIndented
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new VersionConverter());
        options.Converters.Add(new LenientBooleanConverter());
        options.Converters.Add(new LenientStringConverter());
        return options;
    }

    /// <summary>Serialize a route to a PascalCase JSON string.</summary>
    public static string SerializeRoute(RouteConfig route, bool indented = false)
        => JsonSerializer.Serialize(route, indented ? IndentedOptions : Options);

    /// <summary>Serialize a cluster to a PascalCase JSON string.</summary>
    public static string SerializeCluster(ClusterConfig cluster, bool indented = false)
        => JsonSerializer.Serialize(cluster, indented ? IndentedOptions : Options);

    /// <summary>Deserialize a route from a JSON string (comments and trailing commas allowed).</summary>
    public static RouteConfig? DeserializeRoute(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<RouteConfig>(json, Options);

    /// <summary>Deserialize a cluster from a JSON string (comments and trailing commas allowed).</summary>
    public static ClusterConfig? DeserializeCluster(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ClusterConfig>(json, Options);

    /// <summary>Deserialize a route from a parsed JSON element.</summary>
    public static RouteConfig? DeserializeRoute(JsonElement element)
        => element.ValueKind == JsonValueKind.Undefined ? null : element.Deserialize<RouteConfig>(Options);

    /// <summary>Deserialize a cluster from a parsed JSON element.</summary>
    public static ClusterConfig? DeserializeCluster(JsonElement element)
        => element.ValueKind == JsonValueKind.Undefined ? null : element.Deserialize<ClusterConfig>(Options);

    /// <summary>
    /// Handles <see cref="Version"/> values written as relaxed strings such as "2" by
    /// normalizing them to a parseable form, and writes them back as plain strings.
    /// </summary>
    private sealed class VersionConverter : JsonConverter<Version>
    {
        public override Version? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var raw = reader.GetString();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var normalized = raw.Contains('.') ? raw : raw + ".0";
            return Version.TryParse(normalized, out var version) ? version : null;
        }

        public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }

    /// <summary>
    /// Accepts booleans written either as native JSON booleans or as relaxed strings
    /// (e.g. "true"/"false"/"True"/"False"), matching the canonical YARP appsettings style.
    /// Also tolerates 0/1 numeric forms. Writes back as native JSON booleans.
    /// This converter is also used for <see cref="Nullable{Boolean}"/> via the framework's
    /// nullable converter, which delegates element reads to this instance.
    /// </summary>
    private sealed class LenientBooleanConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.String:
                    var raw = reader.GetString();
                    if (bool.TryParse(raw, out var parsed)) return parsed;
                    if (string.Equals(raw, "1", StringComparison.Ordinal)) return true;
                    if (string.Equals(raw, "0", StringComparison.Ordinal)) return false;
                    throw new JsonException($"Cannot convert string '{raw}' to boolean.");
                case JsonTokenType.Number:
                    return reader.GetInt32() != 0;
                default:
                    throw new JsonException($"Cannot convert token '{reader.TokenType}' to boolean.");
            }
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
            => writer.WriteBooleanValue(value);
    }

    /// <summary>
    /// Accepts string values written as native JSON booleans, numbers, or nulls, converting them
    /// to their string representation. This mirrors how Microsoft.Extensions.Configuration binds
    /// scalar values into <c>Dictionary&lt;string, string&gt;</c> (used by YARP Transforms and Metadata).
    /// For example, <c>"RequestHeadersCopy": true</c> becomes the string <c>"true"</c>.
    /// Native JSON strings pass through unchanged.
    /// </summary>
    private sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
                JsonTokenType.Null => null,
                _ => throw new JsonException($"Cannot convert token '{reader.TokenType}' to string.")
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }
}
