using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>
/// Handles batch writes to proxy_logs_meta and proxy_logs_body tables.
/// Schema-adaptive: discovers actual table columns at init and builds SQL dynamically.
/// </summary>
internal sealed class SqliteProxyLogBatchWriter
{
    private readonly SqliteConnectionFactory _connections;
    private readonly ILogger _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private HashSet<string> _metaColumns = new(StringComparer.OrdinalIgnoreCase);
    private string _insertMetaColumns = null!;
    private string _insertMetaValues = null!;

    private static readonly (string Param, string SqlName, Func<ProxyLogMetaEntity, bool> HasValue)[] AllMetaColumns =
    [
        ("@ts",  "Timestamp",          _ => true),
        ("@et",  "EventType",          _ => true),
        ("@lv",  "Level",              _ => true),
        ("@ri",  "RouteId",            m => m.RouteId != null),
        ("@ci",  "ClusterId",          m => m.ClusterId != null),
        ("@mt",  "Method",             m => m.Method != null),
        ("@up",  "UpstreamPath",       m => m.UpstreamPath != null),
        ("@sc",  "StatusCode",         _ => true),
        ("@em",  "ElapsedMs",          _ => true),
        ("@ti",  "TraceId",            m => m.TraceId != null),
        ("@hrb", "HasRequestBody",     _ => true),
        ("@hsb", "HasResponseBody",    _ => true),
        ("@du",  "DownstreamUrl",      m => m.DownstreamUrl != null),
    ];

    public SqliteProxyLogBatchWriter(SqliteConnectionFactory connections, ILogger logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "proxy_logs_meta", ct);
            await DiscoverMetaColumnsAsync(ct);
            _initialized = true;
        }
        finally { _initLock.Release(); }
    }

    public HashSet<string> MetaColumns => _metaColumns;

    public async Task WriteBatchAsync(
        IEnumerable<ProxyLogMetaEntity> metaEntries,
        IEnumerable<ProxyLogBodyEntity> bodyEntries,
        CancellationToken ct)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();
        try
        {
            foreach (var meta in metaEntries)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandTimeout = 30;
                cmd.CommandText = $"INSERT INTO proxy_logs_meta ({_insertMetaColumns}) VALUES ({_insertMetaValues})";
                AddMetaParams(cmd, meta);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            var metaCount = metaEntries.TryGetNonEnumeratedCount(out var c) ? c : metaEntries.Count();
            if (metaCount > 0 && bodyEntries.Any())
            {
                await using var idCmd = conn.CreateCommand();
                idCmd.Transaction = tx;
                idCmd.CommandText = """
                    SELECT Id FROM proxy_logs_meta
                    WHERE Id > (SELECT COALESCE(MAX(Id), 0) FROM proxy_logs_meta) - @count
                    ORDER BY Id
                    """;
                idCmd.Parameters.AddWithValue("@count", metaCount);
                var metaIds = new List<long>();
                await using (var reader = await idCmd.ExecuteReaderAsync(ct))
                {
                    while (await reader.ReadAsync(ct))
                        metaIds.Add(reader.GetInt64(0));
                }

                var bodyList = bodyEntries.ToList();
                for (int i = 0; i < bodyList.Count && i < metaIds.Count; i++)
                {
                    bodyList[i].MetaId = metaIds[i];
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandTimeout = 30;
                    cmd.CommandText = """
                        INSERT INTO proxy_logs_body (MetaId, Message, RequestBody, ResponseBody, RequestHeaders, ResponseHeaders, DownstreamBody, Exception)
                        VALUES (@mid, @msg, @rb, @rsb, @rh, @rsh, @db, @exc)
                        """;
                    AddBodyParams(cmd, bodyList[i]);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task DiscoverMetaColumnsAsync(CancellationToken ct)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var pragmaCmd = conn.CreateCommand();
        pragmaCmd.CommandText = "SELECT name FROM pragma_table_info('proxy_logs_meta')";
        await using var reader = await pragmaCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            columns.Add(reader.GetString(0));

        _metaColumns = columns;

        var insertColumns = new List<string>();
        var insertValues = new List<string>();
        foreach (var (param, sqlName, _) in AllMetaColumns)
        {
            if (columns.Contains(sqlName))
            {
                insertColumns.Add(sqlName);
                insertValues.Add(param);
            }
        }

        _insertMetaColumns = string.Join(", ", insertColumns);
        _insertMetaValues = string.Join(", ", insertValues);

        var missing = AllMetaColumns.Where(c => !columns.Contains(c.SqlName)).Select(c => c.SqlName).ToList();
        if (missing.Count > 0)
            _logger.LogWarning("proxy_logs_meta is missing columns: {Missing}", string.Join(", ", missing));
    }

    private static void AddParamIfColumn(SqliteCommand cmd, HashSet<string> columns, string paramName, string sqlName, object? value)
    {
        if (columns.Contains(sqlName))
            cmd.Parameters.AddWithValue(paramName, value ?? DBNull.Value);
    }

    private void AddMetaParams(SqliteCommand cmd, ProxyLogMetaEntity meta)
    {
        var cols = _metaColumns;
        AddParamIfColumn(cmd, cols, "@ts",  "Timestamp",          (object?)meta.Timestamp ?? "");
        AddParamIfColumn(cmd, cols, "@et",  "EventType",          (object?)meta.EventType ?? "");
        AddParamIfColumn(cmd, cols, "@lv",  "Level",              (object?)meta.Level ?? "");
        AddParamIfColumn(cmd, cols, "@ri",  "RouteId",            meta.RouteId);
        AddParamIfColumn(cmd, cols, "@ci",  "ClusterId",          meta.ClusterId);
        AddParamIfColumn(cmd, cols, "@mt",  "Method",             meta.Method);
        AddParamIfColumn(cmd, cols, "@up",  "UpstreamPath",       meta.UpstreamPath);
        AddParamIfColumn(cmd, cols, "@sc",  "StatusCode",         meta.StatusCode);
        AddParamIfColumn(cmd, cols, "@em",  "ElapsedMs",          meta.ElapsedMs);
        AddParamIfColumn(cmd, cols, "@ti",  "TraceId",            meta.TraceId);
        AddParamIfColumn(cmd, cols, "@hrb", "HasRequestBody",     meta.HasRequestBody);
        AddParamIfColumn(cmd, cols, "@hsb", "HasResponseBody",    meta.HasResponseBody);
        AddParamIfColumn(cmd, cols, "@du",  "DownstreamUrl",      meta.DownstreamUrl);
    }

    private static void AddBodyParams(SqliteCommand cmd, ProxyLogBodyEntity body)
    {
        cmd.Parameters.AddWithValue("@mid", body.MetaId);
        cmd.Parameters.AddWithValue("@msg", (object?)body.Message ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rb", (object?)body.RequestBody ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rsb", (object?)body.ResponseBody ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rh", (object?)body.RequestHeaders ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rsh", (object?)body.ResponseHeaders ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@db", (object?)body.DownstreamBody ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@exc", (object?)body.Exception ?? DBNull.Value);
    }
}
