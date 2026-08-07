using System.Text.Json;
using Aneiang.Yarp.Plugins;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>
/// Migrates circuit-breaker config from schema v1 to v2.
/// v2 adds <c>circuitOpenStatusCodes</c> and <c>onOpenWebhookUrl</c> fields.
/// </summary>
public sealed class CircuitBreakerConfigMigrator : IPluginConfigurationMigrator
{
    public string PluginId => "circuit-breaker";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public bool TryMigrate(string configJson, out string migratedConfigJson, out string error) =>
        ConfigMigratorHelper.TryMigrate(configJson, out migratedConfigJson, out error, (doc, writer) =>
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
                prop.WriteTo(writer);

            if (!doc.RootElement.TryGetProperty("circuitOpenStatusCodes", out _))
            {
                writer.WriteStartArray("circuitOpenStatusCodes");
                foreach (var code in new[] { 502, 503, 504 })
                    writer.WriteNumberValue(code);
                writer.WriteEndArray();
            }

            if (!doc.RootElement.TryGetProperty("onOpenWebhookUrl", out _))
                writer.WriteString("onOpenWebhookUrl", (string?)null);
        });
}

/// <summary>
/// Migrates request-retry config from schema v1 to v2.
/// v2 renames <c>backoffBaseMs</c> to <c>initialBackoffMs</c> and adds <c>maxBackoffMs</c>.
/// </summary>
public sealed class RetryConfigMigrator : IPluginConfigurationMigrator
{
    public string PluginId => "request-retry";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public bool TryMigrate(string configJson, out string migratedConfigJson, out string error) =>
        ConfigMigratorHelper.TryMigrate(configJson, out migratedConfigJson, out error, (doc, writer) =>
        {
            long? oldBackoffBaseMs = null;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("backoffBaseMs"))
                {
                    oldBackoffBaseMs = prop.Value.GetInt64();
                    writer.WriteNumber("initialBackoffMs", oldBackoffBaseMs.Value);
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }

            if (!doc.RootElement.TryGetProperty("initialBackoffMs", out _)
                && !doc.RootElement.TryGetProperty("backoffBaseMs", out _))
            {
                writer.WriteNumber("initialBackoffMs", 100);
            }

            if (!doc.RootElement.TryGetProperty("maxBackoffMs", out _))
            {
                var baseMs = oldBackoffBaseMs ?? 100;
                writer.WriteNumber("maxBackoffMs", baseMs * 10);
            }
        });
}

/// <summary>
/// Migrates waf config from schema v1 to v2.
/// v2 adds <c>enableBotDetection</c>, <c>enableRateLimitIntegration</c> and <c>blockedAction</c> fields.
/// </summary>
public sealed class WafConfigMigrator : IPluginConfigurationMigrator
{
    public string PluginId => "waf";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public bool TryMigrate(string configJson, out string migratedConfigJson, out string error) =>
        ConfigMigratorHelper.TryMigrate(configJson, out migratedConfigJson, out error, (doc, writer) =>
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
                prop.WriteTo(writer);

            if (!doc.RootElement.TryGetProperty("enableBotDetection", out _))
                writer.WriteBoolean("enableBotDetection", false);

            if (!doc.RootElement.TryGetProperty("enableRateLimitIntegration", out _))
                writer.WriteBoolean("enableRateLimitIntegration", false);

            if (!doc.RootElement.TryGetProperty("blockedAction", out _))
                writer.WriteString("blockedAction", "Block");
        });
}

/// <summary>
/// Migrates rate-limit config from schema v1 to v2.
/// v2 adds <c>ipHeaderName</c> for custom IP extraction and <c>exemptIps</c> list.
/// </summary>
public sealed class RateLimitConfigMigrator : IPluginConfigurationMigrator
{
    public string PluginId => "rate-limit";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public bool TryMigrate(string configJson, out string migratedConfigJson, out string error) =>
        ConfigMigratorHelper.TryMigrate(configJson, out migratedConfigJson, out error, (doc, writer) =>
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
                prop.WriteTo(writer);

            if (!doc.RootElement.TryGetProperty("ipHeaderName", out _))
                writer.WriteNull("ipHeaderName");

            if (!doc.RootElement.TryGetProperty("exemptIps", out _))
            {
                writer.WriteStartArray("exemptIps");
                writer.WriteEndArray();
            }
        });
}

/// <summary>
/// Migrates proxy-log config from schema v1 to v2.
/// v2 adds <c>captureGrpcMetadata</c> and <c>redactAuthorizationHeader</c> fields.
/// </summary>
public sealed class ProxyLogConfigMigrator : IPluginConfigurationMigrator
{
    public string PluginId => "proxy-log";
    public int FromVersion => 1;
    public int ToVersion => 2;

    public bool TryMigrate(string configJson, out string migratedConfigJson, out string error) =>
        ConfigMigratorHelper.TryMigrate(configJson, out migratedConfigJson, out error, (doc, writer) =>
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
                prop.WriteTo(writer);

            if (!doc.RootElement.TryGetProperty("captureGrpcMetadata", out _))
                writer.WriteBoolean("captureGrpcMetadata", false);

            if (!doc.RootElement.TryGetProperty("redactAuthorizationHeader", out _))
                writer.WriteBoolean("redactAuthorizationHeader", true);
        });
}

/// <summary>
/// Shared helper that parses config JSON, invokes a mutation callback, and serializes the result.
/// </summary>
internal static class ConfigMigratorHelper
{
    public delegate void MigrateAction(JsonDocument doc, Utf8JsonWriter writer);

    public static bool TryMigrate(string configJson, out string migratedConfigJson, out string error, MigrateAction action)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                action(doc, writer);
                writer.WriteEndObject();
                writer.Flush();
            }

            migratedConfigJson = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            migratedConfigJson = configJson;
            error = ex.Message;
            return false;
        }
    }
}
