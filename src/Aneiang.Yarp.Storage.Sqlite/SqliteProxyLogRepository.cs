using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>
/// SQLite implementation of IProxyLogRepository.
/// Handles batch writes, paginated queries, detail retrieval, cleanup, and stats aggregation
/// against proxy_logs_meta and proxy_logs_body tables.
/// Uses SqliteConnectionFactory for pooled connections with WAL mode.
/// Schema-adaptive: discovers actual table columns at init and builds SQL dynamically.
/// </summary>
public sealed class SqliteProxyLogRepository : IProxyLogRepository
{
    private readonly SqliteConnectionFactory _connections;
    private readonly ILogger<SqliteProxyLogRepository> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // Cached at init: actual column names from proxy_logs_meta (case-insensitive) for adaptive SQL construction
    private HashSet<string> _metaColumns = null!;
    // Pre-built INSERT column list and parameter list for batch writes
    private string _insertMetaColumns = null!;
    private string _insertMetaValues = null!;

    public SqliteProxyLogRepository(SqliteConnectionFactory connections, ILogger<SqliteProxyLogRepository> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    /// <summary>
    /// All possible meta columns: (ParameterName, SqlColumnName, HasValue predicate).
    /// SqlColumnName must match the actual column name in the proxy_logs_meta table (case-insensitive via OrdinalIgnoreCase).
    /// </summary>
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

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
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

    /// <summary>
    /// Discovers actual columns in proxy_logs_meta and builds the dynamic INSERT template.
    /// Uses case-insensitive comparison because SQLite identifiers are case-insensitive.
    /// </summary>
    private async Task DiscoverMetaColumnsAsync(CancellationToken ct)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);

        // Discover actual columns (pragma_table_info returns stored names)
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var pragmaCmd = conn.CreateCommand();
        pragmaCmd.CommandText = "SELECT name FROM pragma_table_info('proxy_logs_meta')";
        await using var reader = await pragmaCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            columns.Add(reader.GetString(0));

        _metaColumns = columns;

        // Build dynamic INSERT: only include columns that exist
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

        // Log missing columns (info only, writes are schema-adaptive so no error)
        var missing = AllMetaColumns.Where(c => !columns.Contains(c.SqlName)).Select(c => c.SqlName).ToList();
        if (missing.Count > 0)
        {
            _logger.LogWarning(
                "proxy_logs_meta is missing columns: {Missing}. Persistence will work but these fields will not be stored. Consider running schema migration.",
                string.Join(", ", missing));
        }
    }

    /// <summary>Checks whether a column name exists (case-insensitive).</summary>
    private bool HasColumn(string columnName) => _metaColumns.Contains(columnName);

    /// <summary>
    /// Adds a parameter only if the target column exists in the table.
    /// </summary>
    private static void AddParamIfColumn(SqliteCommand cmd, HashSet<string> columns, string paramName, string sqlName, object? value)
    {
        if (columns.Contains(sqlName))
            cmd.Parameters.AddWithValue(paramName, value ?? DBNull.Value);
    }

    /// <inheritdoc />
    public async Task WriteBatchAsync(
        IEnumerable<ProxyLogMetaEntity> metaEntries,
        IEnumerable<ProxyLogBodyEntity> bodyEntries,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();
        try
        {
            // Insert meta rows using dynamically-built column list
            foreach (var meta in metaEntries)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandTimeout = 30;
                cmd.CommandText = $"INSERT INTO proxy_logs_meta ({_insertMetaColumns}) VALUES ({_insertMetaValues})";
                AddMetaParams(cmd, meta);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Insert body rows (linked to meta by MetaId — meta.Id is auto-incremented)
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

    /// <inheritdoc />
    public async Task<ProxyLogBodyEntity?> GetBodyByMetaIdAsync(long metaId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM proxy_logs_body WHERE MetaId = @id";
        cmd.Parameters.AddWithValue("@id", metaId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapBody(reader) : null;
    }

    /// <inheritdoc />
    public async Task<ProxyLogMetaEntity?> GetMetaByIdAsync(long metaId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM proxy_logs_meta WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", metaId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapMeta(reader) : null;
    }

    /// <inheritdoc />
    public async Task<(List<ProxyLogMetaEntity> Items, int TotalCount)> SearchAsync(
        int page, int pageSize,
        string? routeId = null, string? clusterId = null, string? level = null,
        int? statusCodeMin = null, int? statusCodeMax = null,
        DateTime? startTime = null, DateTime? endTime = null,
        string? keyword = null, string? eventType = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);

        var where = BuildWhereClause(routeId, clusterId, level, statusCodeMin, statusCodeMax, startTime, endTime, keyword, eventType);
        var paramList = BuildSearchParams(routeId, clusterId, level, statusCodeMin, statusCodeMax, startTime, endTime, keyword, eventType);

        // Count query
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM proxy_logs_meta WHERE {where}";
        foreach (var p in paramList) countCmd.Parameters.Add(p);
        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        // Data query
        var offset = (page - 1) * pageSize;
        await using var dataCmd = conn.CreateCommand();
        dataCmd.CommandText = $"SELECT * FROM proxy_logs_meta WHERE {where} ORDER BY Timestamp DESC LIMIT @lim OFFSET @off";
        dataCmd.Parameters.AddWithValue("@lim", pageSize);
        dataCmd.Parameters.AddWithValue("@off", offset);
        foreach (var p in paramList) dataCmd.Parameters.Add(p);

        var items = new List<ProxyLogMetaEntity>();
        await using (var reader = await dataCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                items.Add(MapMeta(reader));
        }

        return (items, totalCount);
    }

    /// <inheritdoc />
    public async Task CleanupAsync(int metaRetentionDays, int bodyRetentionDays, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();
        try
        {
            // Delete expired meta (CASCADE automatically deletes associated body rows)
            await using var metaCmd = conn.CreateCommand();
            metaCmd.Transaction = tx;
            metaCmd.CommandTimeout = 60;
            metaCmd.CommandText = "DELETE FROM proxy_logs_meta WHERE Timestamp < datetime('now', @metaDays || ' days')";
            metaCmd.Parameters.AddWithValue("@metaDays", $"-{metaRetentionDays}");
            var metaDeleted = await metaCmd.ExecuteNonQueryAsync(ct);

            // Delete expired body rows independently (body retention may be shorter than meta)
            await using var bodyCmd = conn.CreateCommand();
            bodyCmd.Transaction = tx;
            bodyCmd.CommandTimeout = 60;
            bodyCmd.CommandText = """
                DELETE FROM proxy_logs_body
                WHERE MetaId IN (
                    SELECT m.Id FROM proxy_logs_meta m
                    WHERE m.Timestamp < datetime('now', @bodyDays || ' days')
                )
                """;
            bodyCmd.Parameters.AddWithValue("@bodyDays", $"-{bodyRetentionDays}");
            var bodyDeleted = await bodyCmd.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
            _logger.LogInformation("Log cleanup: deleted {MetaDeleted} meta rows and {BodyDeleted} body rows (meta={MetaDays}d, body={BodyDays}d)",
                metaDeleted, bodyDeleted, metaRetentionDays, bodyRetentionDays);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task CheckpointAsync(CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async Task<ProxyLogStatsResult> GetStatsAsync(int recentMinutes, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        // If StatusCode column doesn't exist, stats queries can't run — return empty.
        if (!HasColumn("StatusCode"))
            return new ProxyLogStatsResult();

        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);

        var fromTime = DateTime.UtcNow.AddMinutes(-recentMinutes).ToString("O");

        var eventFilter = HasColumn("EventType") ? "AND EventType = 'ProxyResponse'" : "";

        await using var statsCmd = conn.CreateCommand();
        statsCmd.CommandText = $"""
            SELECT
                COUNT(*) as total,
                SUM(CASE WHEN StatusCode BETWEEN 200 AND 399 THEN 1 ELSE 0 END) as success,
                SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) as errors,
                AVG(CASE WHEN ElapsedMs > 0 THEN ElapsedMs ELSE NULL END) as avg_latency,
                COUNT(*) as recent_count
            FROM proxy_logs_meta
            WHERE Timestamp >= @from {eventFilter}
            """;
        statsCmd.Parameters.AddWithValue("@from", fromTime);
        await using var reader = await statsCmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        var result = new ProxyLogStatsResult
        {
            TotalRequests = reader.GetInt64(0),
            SuccessCount = reader.GetInt64(1),
            ErrorCount = reader.GetInt64(2),
            AvgLatencyMs = reader.IsDBNull(3) ? 0 : reader.GetDouble(3)
        };

        var recentMinutesSpan = recentMinutes > 0 ? recentMinutes : 1;
        result.RequestsPerMinute = result.TotalRequests > 0 ? (int)(result.TotalRequests / recentMinutesSpan) : 0;

        if (HasColumn("ElapsedMs") && HasColumn("EventType"))
        {
            result.P50LatencyMs = await GetPercentileAsync(conn, fromTime, 0.50, ct);
            result.P90LatencyMs = await GetPercentileAsync(conn, fromTime, 0.90, ct);
            result.P99LatencyMs = await GetPercentileAsync(conn, fromTime, 0.99, ct);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<List<ProxyLogTrafficBucket>> GetTrafficDataAsync(DateTime startTime, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!HasColumn("StatusCode")) return [];

        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var eventFilter = HasColumn("EventType") ? "AND EventType = 'ProxyResponse'" : "";
        cmd.CommandText = $"""
            SELECT
                strftime('%Y-%m-%d %H:%M', Timestamp) as time_bucket,
                COUNT(*) as request_count,
                SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) as error_count
            FROM proxy_logs_meta
            WHERE Timestamp >= @from {eventFilter}
            GROUP BY time_bucket
            ORDER BY time_bucket
            """;
        cmd.Parameters.AddWithValue("@from", startTime.ToString("O"));

        var result = new List<ProxyLogTrafficBucket>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ProxyLogTrafficBucket
            {
                TimeBucket = DateTime.Parse(reader.GetString(0)),
                RequestCount = reader.GetInt32(1),
                ErrorCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2)
            });
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<List<ProxyLogRouteIssue>> GetTopIssuesAsync(DateTime startTime, int count, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!HasColumn("StatusCode") || !HasColumn("RouteId")) return [];

        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var eventFilter = HasColumn("EventType") ? "AND EventType = 'ProxyResponse'" : "";
        cmd.CommandText = $"""
            SELECT
                RouteId,
                COUNT(*) as total_count,
                SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) as error_count
            FROM proxy_logs_meta
            WHERE Timestamp >= @from {eventFilter}
            GROUP BY RouteId
            ORDER BY error_count DESC
            LIMIT @lim
            """;
        cmd.Parameters.AddWithValue("@from", startTime.ToString("O"));
        cmd.Parameters.AddWithValue("@lim", count);

        var result = new List<ProxyLogRouteIssue>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ProxyLogRouteIssue
            {
                RouteId = reader.IsDBNull(0) ? null : reader.GetString(0),
                TotalCount = reader.GetInt32(1),
                ErrorCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2)
            });
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<int> GetRecent5xxCountAsync(int minutes, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!HasColumn("StatusCode")) return 0;

        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var eventFilter = HasColumn("EventType") ? "AND EventType = 'ProxyResponse'" : "";
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM proxy_logs_meta
            WHERE Timestamp >= datetime('now', @mins || ' minutes')
              AND StatusCode >= 500
              {eventFilter}
            """;
        cmd.Parameters.AddWithValue("@mins", $"-{minutes}");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    // ---- Helper methods ----

    private static async Task<double> GetPercentileAsync(SqliteConnection conn, string fromTime, double percentile, CancellationToken ct)
    {
        // Get total count for offset calculation
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = """
            SELECT COUNT(*) FROM proxy_logs_meta
            WHERE Timestamp >= @from AND EventType = 'ProxyResponse' AND ElapsedMs > 0
            """;
        countCmd.Parameters.AddWithValue("@from", fromTime);
        var total = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));

        if (total == 0) return 0;

        var offset = (int)(total * percentile);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ElapsedMs FROM proxy_logs_meta
            WHERE Timestamp >= @from AND EventType = 'ProxyResponse' AND ElapsedMs > 0
            ORDER BY ElapsedMs
            LIMIT 1 OFFSET @off
            """;
        cmd.Parameters.AddWithValue("@from", fromTime);
        cmd.Parameters.AddWithValue("@off", offset);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is DBNull or null ? 0 : Convert.ToDouble(result);
    }

    private string BuildWhereClause(
        string? routeId, string? clusterId, string? level,
        int? statusCodeMin, int? statusCodeMax,
        DateTime? startTime, DateTime? endTime,
        string? keyword, string? eventType)
    {
        var conditions = new List<string> { "1=1" };
        if (routeId != null && HasColumn("RouteId")) conditions.Add("RouteId = @ri");
        if (clusterId != null && HasColumn("ClusterId")) conditions.Add("ClusterId = @ci");
        if (level != null) conditions.Add("Level = @lv");
        if (statusCodeMin != null && HasColumn("StatusCode")) conditions.Add("StatusCode >= @scmin");
        if (statusCodeMax != null && HasColumn("StatusCode")) conditions.Add("StatusCode <= @scmax");
        if (startTime != null) conditions.Add("Timestamp >= @st");
        if (endTime != null) conditions.Add("Timestamp <= @et");
        if (keyword != null)
        {
            var kwParts = new List<string>();
            if (HasColumn("UpstreamPath")) kwParts.Add("UpstreamPath LIKE @kw");
            if (HasColumn("TraceId")) kwParts.Add("TraceId LIKE @kw2");
            if (kwParts.Count > 0) conditions.Add($"({string.Join(" OR ", kwParts)})");
        }
        if (eventType != null && HasColumn("EventType")) conditions.Add("EventType = @evt");
        return string.Join(" AND ", conditions);
    }

    private List<SqliteParameter> BuildSearchParams(
        string? routeId, string? clusterId, string? level,
        int? statusCodeMin, int? statusCodeMax,
        DateTime? startTime, DateTime? endTime,
        string? keyword, string? eventType)
    {
        var params_ = new List<SqliteParameter>();
        if (routeId != null && HasColumn("RouteId")) params_.Add(new SqliteParameter("@ri", routeId));
        if (clusterId != null && HasColumn("ClusterId")) params_.Add(new SqliteParameter("@ci", clusterId));
        if (level != null) params_.Add(new SqliteParameter("@lv", level));
        if (statusCodeMin != null && HasColumn("StatusCode")) params_.Add(new SqliteParameter("@scmin", statusCodeMin.Value));
        if (statusCodeMax != null && HasColumn("StatusCode")) params_.Add(new SqliteParameter("@scmax", statusCodeMax.Value));
        if (startTime != null) params_.Add(new SqliteParameter("@st", startTime.Value.ToString("O")));
        if (endTime != null) params_.Add(new SqliteParameter("@et", endTime.Value.ToString("O")));
        if (keyword != null)
        {
            if (HasColumn("UpstreamPath")) params_.Add(new SqliteParameter("@kw", $"%{keyword}%"));
            if (HasColumn("TraceId")) params_.Add(new SqliteParameter("@kw2", $"%{keyword}%"));
        }
        if (eventType != null && HasColumn("EventType")) params_.Add(new SqliteParameter("@evt", eventType));
        return params_;
    }

    private void AddMetaParams(SqliteCommand cmd, ProxyLogMetaEntity meta)
    {
        var cols = _metaColumns;
        // NOT NULL columns: ensure we never pass DBNull (which AddParamIfColumn would do for null)
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

    private ProxyLogMetaEntity MapMeta(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")),
        Timestamp = SafeReadString(r, "Timestamp") ?? string.Empty,
        EventType = SafeReadString(r, "EventType") ?? string.Empty,
        Level = SafeReadString(r, "Level") ?? string.Empty,
        RouteId = SafeReadString(r, "RouteId"),
        ClusterId = SafeReadString(r, "ClusterId"),
        Method = SafeReadString(r, "Method"),
        UpstreamPath = SafeReadString(r, "UpstreamPath"),
        StatusCode = SafeReadInt32(r, "StatusCode"),
        ElapsedMs = SafeReadDouble(r, "ElapsedMs"),
        TraceId = SafeReadString(r, "TraceId"),
        HasRequestBody = SafeReadInt32(r, "HasRequestBody"),
        HasResponseBody = SafeReadInt32(r, "HasResponseBody"),
        DownstreamUrl = SafeReadString(r, "DownstreamUrl"),
    };

    private static ProxyLogBodyEntity MapBody(SqliteDataReader r) => new()
    {
        MetaId = r.GetInt64(r.GetOrdinal("MetaId")),
        Message = ReadString(r, "Message"),
        RequestBody = ReadString(r, "RequestBody"),
        ResponseBody = ReadString(r, "ResponseBody"),
        RequestHeaders = ReadString(r, "RequestHeaders"),
        ResponseHeaders = ReadString(r, "ResponseHeaders"),
        DownstreamBody = ReadString(r, "DownstreamBody"),
        Exception = ReadString(r, "Exception")
    };

    private static string? ReadString(SqliteDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }

    /// <summary>Safely reads a string column; returns null if column doesn't exist.</summary>
    private string? SafeReadString(SqliteDataReader r, string name)
        => HasColumn(name) ? ReadString(r, name) : null;

    /// <summary>Safely reads an int column; returns 0 if column doesn't exist.</summary>
    private int SafeReadInt32(SqliteDataReader r, string name)
    {
        if (!HasColumn(name)) return 0;
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? 0 : r.GetInt32(ordinal);
    }

    /// <summary>Safely reads a double column; returns 0 if column doesn't exist.</summary>
    private double SafeReadDouble(SqliteDataReader r, string name)
    {
        if (!HasColumn(name)) return 0;
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? 0 : r.GetDouble(ordinal);
    }
}
