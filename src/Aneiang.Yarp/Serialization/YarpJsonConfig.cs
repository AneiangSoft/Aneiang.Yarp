using System.Text.Json;
using System.Text.Json.Serialization;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Serialization;

public static class YarpJsonConfig
{
    public static JsonSerializerOptions Options { get; } = BuildOptions(writeIndented: false);

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

    public static string SerializeRoute(RouteConfig route, bool indented = false)
        => JsonSerializer.Serialize(route, indented ? IndentedOptions : Options);

    public static string SerializeCluster(ClusterConfig cluster, bool indented = false)
        => JsonSerializer.Serialize(cluster, indented ? IndentedOptions : Options);

    public static RouteConfig? DeserializeRoute(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<RouteConfig>(json, Options);

    public static ClusterConfig? DeserializeCluster(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ClusterConfig>(json, Options);

    public static RouteConfig? DeserializeRoute(JsonElement element)
        => element.ValueKind == JsonValueKind.Undefined ? null : element.Deserialize<RouteConfig>(Options);

    public static ClusterConfig? DeserializeCluster(JsonElement element)
        => element.ValueKind == JsonValueKind.Undefined ? null : element.Deserialize<ClusterConfig>(Options);

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
