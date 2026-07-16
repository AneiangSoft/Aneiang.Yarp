using System.Text;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Helper methods for common string building patterns.
/// </summary>
internal static class StringBuilderExtensions
{
    /// <summary>
    /// Appends a key-value pair with separator efficiently.
    /// </summary>
    public static StringBuilder AppendKeyValue(this StringBuilder sb, string key, string? value, string separator = ", ")
    {
        if (sb.Length > 0)
            sb.Append(separator);
        sb.Append(key).Append('=').Append(value ?? "null");
        return sb;
    }

    /// <summary>
    /// Appends a query string parameter.
    /// </summary>
    public static StringBuilder AppendQueryParam(this StringBuilder sb, string key, string? value, bool isFirst = false)
    {
        if (!isFirst)
            sb.Append('&');
        sb.Append(key).Append('=').Append(Uri.EscapeDataString(value ?? ""));
        return sb;
    }

    /// <summary>
    /// Appends a JSON property name and value.
    /// </summary>
    public static StringBuilder AppendJsonProperty(this StringBuilder sb, string name, string? value, bool isFirst = false)
    {
        if (!isFirst)
            sb.Append(',');
        sb.Append('"').Append(name).Append("\":\"");
        if (!string.IsNullOrEmpty(value))
        {
            // Basic JSON escaping
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
        }
        sb.Append('"');
        return sb;
    }
}
