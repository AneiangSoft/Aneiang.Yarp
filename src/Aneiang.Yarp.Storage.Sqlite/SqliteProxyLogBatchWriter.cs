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
        IReadOnlyList<ProxyLogMetaEntity> metaEntries,
        IReadOnlyList<ProxyLogBodyEntity?> bodyEntries,
        CancellationToken ct)
    {
        if (metaEntries.Count != bodyEntries.Count)
            throw new ArgumentException("Meta and body entry counts must match.");
        if (metaEntries.Count == 0)
            return;

        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();
        try
        {
            await using var metaCmd = CreateMetaCommand(conn, tx);
            await using var bodyCmd = CreateBodyCommand(conn, tx);

            for (var i = 0; i < metaEntries.Count; i++)
            {
                SetMetaParams(metaCmd, metaEntries[i]);
                var result = await metaCmd.ExecuteScalarAsync(ct);
                var metaId = Convert.ToInt64(result);

                var body = bodyEntries[i];
                if (body == null)
                    continue;

                body.MetaId = metaId;
                SetBodyParams(bodyCmd, body);
                await bodyCmd.ExecuteNonQueryAsync(ct);
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

    private SqliteCommand CreateMetaCommand(SqliteConnection conn, SqliteTransaction tx)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandTimeout = 30;
        cmd.CommandText = $"INSERT INTO proxy_logs_meta ({_insertMetaColumns}) VALUES ({_insertMetaValues}) RETURNING Id";
        foreach (var (param, sqlName, _) in AllMetaColumns)
        {
            if (_metaColumns.Contains(sqlName))
                cmd.Parameters.AddWithValue(param, DBNull.Value);
        }
        return cmd;
    }

    private static SqliteCommand CreateBodyCommand(SqliteConnection conn, SqliteTransaction tx)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandTimeout = 30;
        cmd.CommandText = """
            INSERT INTO proxy_logs_body (MetaId, Message, RequestBody, ResponseBody, RequestHeaders, ResponseHeaders, DownstreamBody, Exception)
            VALUES (@mid, @msg, @rb, @rsb, @rh, @rsh, @db, @exc)
            """;
        foreach (var name in new[] { "@mid", "@msg", "@rb", "@rsb", "@rh", "@rsh", "@db", "@exc" })
            cmd.Parameters.AddWithValue(name, DBNull.Value);
        return cmd;
    }

    private void SetMetaParams(SqliteCommand cmd, ProxyLogMetaEntity meta)
    {
        SetParamIfColumn(cmd, "@ts", "Timestamp", meta.Timestamp ?? "");
        SetParamIfColumn(cmd, "@et", "EventType", meta.EventType ?? "");
        SetParamIfColumn(cmd, "@lv", "Level", meta.Level ?? "");
        SetParamIfColumn(cmd, "@ri", "RouteId", meta.RouteId);
        SetParamIfColumn(cmd, "@ci", "ClusterId", meta.ClusterId);
        SetParamIfColumn(cmd, "@mt", "Method", meta.Method);
        SetParamIfColumn(cmd, "@up", "UpstreamPath", meta.UpstreamPath);
        SetParamIfColumn(cmd, "@sc", "StatusCode", meta.StatusCode);
        SetParamIfColumn(cmd, "@em", "ElapsedMs", meta.ElapsedMs);
        SetParamIfColumn(cmd, "@ti", "TraceId", meta.TraceId);
        SetParamIfColumn(cmd, "@hrb", "HasRequestBody", meta.HasRequestBody);
        SetParamIfColumn(cmd, "@hsb", "HasResponseBody", meta.HasResponseBody);
        SetParamIfColumn(cmd, "@du", "DownstreamUrl", meta.DownstreamUrl);
    }

    private void SetParamIfColumn(SqliteCommand cmd, string paramName, string sqlName, object? value)
    {
        if (_metaColumns.Contains(sqlName))
            cmd.Parameters[paramName].Value = value ?? DBNull.Value;
    }

    private static void SetBodyParams(SqliteCommand cmd, ProxyLogBodyEntity body)
    {
        cmd.Parameters["@mid"].Value = body.MetaId;
        cmd.Parameters["@msg"].Value = (object?)body.Message ?? DBNull.Value;
        cmd.Parameters["@rb"].Value = (object?)body.RequestBody ?? DBNull.Value;
        cmd.Parameters["@rsb"].Value = (object?)body.ResponseBody ?? DBNull.Value;
        cmd.Parameters["@rh"].Value = (object?)body.RequestHeaders ?? DBNull.Value;
        cmd.Parameters["@rsh"].Value = (object?)body.ResponseHeaders ?? DBNull.Value;
        cmd.Parameters["@db"].Value = (object?)body.DownstreamBody ?? DBNull.Value;
        cmd.Parameters["@exc"].Value = (object?)body.Exception ?? DBNull.Value;
    }
}
