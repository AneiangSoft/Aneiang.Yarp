using System.Text.Json;
using System.Text.Json.Serialization;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Models;
using Microsoft.AspNetCore.Hosting;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Services;

/// <summary>
/// Persists gateway policies to a JSON file.
/// </summary>
public class GatewayPolicyPersistenceService : IGatewayPolicyPersistenceService
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GatewayPolicyPersistenceService(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "gateway-policies.json");
    }

    public GatewayPolicyCollection Load()
    {
        if (!File.Exists(_filePath))
            return new GatewayPolicyCollection();

        try
        {
            var json = File.ReadAllText(_filePath);
            return System.Text.Json.JsonSerializer.Deserialize<GatewayPolicyCollection>(json, _jsonOptions)
                   ?? new GatewayPolicyCollection();
        }
        catch
        {
            return new GatewayPolicyCollection();
        }
    }

    public void Save(GatewayPolicyCollection collection)
    {
        collection.LastModified = DateTime.UtcNow;
        var json = System.Text.Json.JsonSerializer.Serialize(collection, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
