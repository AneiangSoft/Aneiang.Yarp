using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Services;

/// <summary>Shared strict JSON deserialization and validation helpers for native plugin adapters.</summary>
internal static class NativeAdapterHelpers
{
    internal static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    static NativeAdapterHelpers()
    {
        StrictJsonOptions.Converters.Add(new JsonStringEnumConverter());
        StrictJsonOptions.Converters.Add(new StrictVersionConverter());
    }

    internal static T Deserialize<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("ConfigJson is required.");
        return JsonSerializer.Deserialize<T>(json, StrictJsonOptions)
            ?? throw new ArgumentException("ConfigJson must contain a JSON object.");
    }

    internal static string Required(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"{name} is required.");

    private sealed class StrictVersionConverter : JsonConverter<Version>
    {
        public override Version? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (!Version.TryParse(value, out var version)) throw new JsonException($"Invalid HTTP version '{value}'.");
            return version;
        }

        public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
