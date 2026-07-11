using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>
/// Handles statistical aggregation queries on proxy_logs_meta:
/// stats, traffic data, top issues, recent 5xx counts.
/// </summary>
internal sealed class SqliteProxyLogAggregator
{
    private readonly SqliteConnectionFactory _connections;
    private readonly SqliteProxyLogBatchWriter _writer;

    public SqliteProxyLogAggregator(SqliteConnectionFactory connections, SqliteProxyLogBatchWriter writer)
    {
        _connections = connections;
        _writer = writer;
    }

    private bool HasColumn(string name) => _writer.MetaColumns.Contains(name);

    public async Task<ProxyLogStatsResult> GetStatsAsync(int recentMinutes, CancellationToken ct)
    {
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

    public async Task<List<ProxyLogTrafficBucket>> GetTrafficDataAsync(DateTime startTime, CancellationToken ct)
    {
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
                TimeBucket = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                RequestCount = reader.GetInt32(1),
                ErrorCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2)
            });
        }
        return result;
    }

    public async Task<List<ProxyLogRouteIssue>> GetTopIssuesAsync(DateTime startTime, int count, CancellationToken ct)
    {
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

    public async Task<int> GetRecent5xxCountAsync(int minutes, CancellationToken ct)
    {
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

    private static async Task<double> GetPercentileAsync(SqliteConnection conn, string fromTime, double percentile, CancellationToken ct)
    {
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
}
