using Aneiang.Yarp.Storage.Entities;
using Microsoft.Data.Sqlite;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>
/// Handles paginated queries and detail retrieval from proxy_logs_meta and proxy_logs_body.
/// </summary>
internal sealed class SqliteProxyLogReader
{
    private readonly SqliteConnectionFactory _connections;
    private readonly SqliteProxyLogBatchWriter _writer; // for MetaColumns access

    public SqliteProxyLogReader(SqliteConnectionFactory connections, SqliteProxyLogBatchWriter writer)
    {
        _connections = connections;
        _writer = writer;
    }

    private bool HasColumn(string name) => _writer.MetaColumns.Contains(name);

    public async Task<ProxyLogBodyEntity?> GetBodyByMetaIdAsync(long metaId, CancellationToken ct)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM proxy_logs_body WHERE MetaId = @id";
        cmd.Parameters.AddWithValue("@id", metaId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapBody(reader) : null;
    }

    public async Task<ProxyLogMetaEntity?> GetMetaByIdAsync(long metaId, CancellationToken ct)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM proxy_logs_meta WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", metaId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapMeta(reader) : null;
    }

    public async Task<(List<ProxyLogMetaEntity> Items, int TotalCount)> SearchAsync(
        int page, int pageSize,
        string? routeId, string? clusterId, string? level,
        int? statusCodeMin, int? statusCodeMax,
        DateTime? startTime, DateTime? endTime,
        string? keyword, string? eventType,
        CancellationToken ct)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);

        var where = BuildWhereClause(routeId, clusterId, level, statusCodeMin, statusCodeMax, startTime, endTime, keyword, eventType);

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM proxy_logs_meta WHERE {where}";
        AddSearchParams(countCmd, routeId, clusterId, level, statusCodeMin, statusCodeMax, startTime, endTime, keyword, eventType);
        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        var offset = (page - 1) * pageSize;
        await using var dataCmd = conn.CreateCommand();
        dataCmd.CommandText = $"SELECT * FROM proxy_logs_meta WHERE {where} ORDER BY Timestamp DESC LIMIT @lim OFFSET @off";
        dataCmd.Parameters.AddWithValue("@lim", pageSize);
        dataCmd.Parameters.AddWithValue("@off", offset);
        AddSearchParams(dataCmd, routeId, clusterId, level, statusCodeMin, statusCodeMax, startTime, endTime, keyword, eventType);

        var items = new List<ProxyLogMetaEntity>(pageSize);
        await using (var reader = await dataCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                items.Add(MapMeta(reader));
        }

        return (items, totalCount);
    }

    // ---- Helpers ----

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

    private string? SafeReadString(SqliteDataReader r, string name)
        => HasColumn(name) ? ReadString(r, name) : null;

    private int SafeReadInt32(SqliteDataReader r, string name)
    {
        if (!HasColumn(name)) return 0;
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? 0 : r.GetInt32(ordinal);
    }

    private double SafeReadDouble(SqliteDataReader r, string name)
    {
        if (!HasColumn(name)) return 0;
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? 0 : r.GetDouble(ordinal);
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

    private void AddSearchParams(
        SqliteCommand command,
        string? routeId, string? clusterId, string? level,
        int? statusCodeMin, int? statusCodeMax,
        DateTime? startTime, DateTime? endTime,
        string? keyword, string? eventType)
    {
        var parameters = command.Parameters;
        if (routeId != null && HasColumn("RouteId")) parameters.AddWithValue("@ri", routeId);
        if (clusterId != null && HasColumn("ClusterId")) parameters.AddWithValue("@ci", clusterId);
        if (level != null) parameters.AddWithValue("@lv", level);
        if (statusCodeMin != null && HasColumn("StatusCode")) parameters.AddWithValue("@scmin", statusCodeMin.Value);
        if (statusCodeMax != null && HasColumn("StatusCode")) parameters.AddWithValue("@scmax", statusCodeMax.Value);
        if (startTime != null) parameters.AddWithValue("@st", startTime.Value.ToString("O"));
        if (endTime != null) parameters.AddWithValue("@et", endTime.Value.ToString("O"));
        if (keyword != null)
        {
            var pattern = $"%{keyword}%";
            if (HasColumn("UpstreamPath")) parameters.AddWithValue("@kw", pattern);
            if (HasColumn("TraceId")) parameters.AddWithValue("@kw2", pattern);
        }
        if (eventType != null && HasColumn("EventType")) parameters.AddWithValue("@evt", eventType);
    }
}
