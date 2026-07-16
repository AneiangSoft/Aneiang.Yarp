using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

/// <summary>
/// Custom JSON converter that serializes HeaderList as {"key":"value"} object format
/// and deserializes back — maintaining backward compatibility with the Dictionary format
/// used by frontend and existing SQLite data.
/// </summary>
public sealed class HeaderListJsonConverter : JsonConverter<HeaderList?>
{
    public override HeaderList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        // Read as JSON object {"key":"value"}
        using var doc = JsonDocument.ParseValue(ref reader);
        var list = new HeaderList(doc.RootElement.EnumerateObject().Count());

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var value = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetString();
            list.Add(prop.Name, value ?? string.Empty);
        }

        return list;
    }

    public override void Write(Utf8JsonWriter writer, HeaderList? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        // Write as JSON object {"key":"value"} — same format as Dictionary<string,string>
        writer.WriteStartObject();
        foreach (var kvp in value)
        {
            writer.WriteString(kvp.Key, kvp.Value);
        }
        writer.WriteEndObject();
    }
}
