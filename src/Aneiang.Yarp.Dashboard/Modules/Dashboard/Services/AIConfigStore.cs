using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;

/// <summary>
/// File-based AI config store. Loads from ai-config.json, falls back to bound IOptions.
/// Updates are persisted to the file and reflected in-memory immediately.
/// </summary>
public class AIConfigStore
{
    private static readonly JsonSerializerOptions FileJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IOptions<AIOptions> _boundOptions;
    private readonly string _configPath;
    private readonly object _lock = new();
    private AIOptions? _current;

    public AIConfigStore(IOptions<AIOptions> boundOptions, IOptions<StorageOptions> storageOptions)
    {
        _boundOptions = boundOptions;
        _configPath = ResolveConfigPath(storageOptions.Value);
        _current = LoadFromFile();
    }

    public AIOptions Current
    {
        get
        {
            lock (_lock)
            {
                return _current ?? _boundOptions.Value;
            }
        }
    }

    public bool IsConfigured => Current.IsConfigured;

    public void Save(AIOptions options)
    {
        lock (_lock)
        {
            _current = options;
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(options, FileJsonOpts);
            File.WriteAllText(_configPath, json);
        }
    }

    private AIOptions? LoadFromFile()
    {
        try
        {
            if (!File.Exists(_configPath)) return null;
            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<AIOptions>(json, FileJsonOpts);
        }
        catch { return null; }
    }

    private static string ResolveConfigPath(StorageOptions storage)
    {
        // Place ai-config.json next to the SQLite database
        var connStr = storage.Sqlite.ConnectionString;
        var dbFileName = "gateway-store.db";
        if (!string.IsNullOrEmpty(connStr))
        {
            var idx = connStr.IndexOf("Data Source=", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var start = idx + "Data Source=".Length;
                var end = connStr.IndexOf(';', start);
                if (end < 0) end = connStr.Length;
                dbFileName = connStr[start..end].Trim();
                if (!Path.IsPathRooted(dbFileName))
                    dbFileName = Path.GetFullPath(dbFileName);
            }
        }
        var dir = Path.GetDirectoryName(dbFileName);
        if (string.IsNullOrEmpty(dir)) dir = AppContext.BaseDirectory;
        return Path.Combine(dir, "ai-config.json");
    }
}
