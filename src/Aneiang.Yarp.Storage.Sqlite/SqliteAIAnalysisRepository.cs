using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Aneiang.Yarp.Storage.Sqlite;

public class SqliteAIAnalysisRepository : IAIAnalysisRepository
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteAIAnalysisRepository(SqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task SaveAnalysisAsync(AIAnalysisEntry entry, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ai_analysis (analysis_type, content, severity, related_routes, related_clusters, created_at)
            VALUES (@type, @content, @sev, @rr, @rc, @at)
            """;
        cmd.Parameters.AddWithValue("@type", entry.AnalysisType);
        cmd.Parameters.AddWithValue("@content", entry.Content);
        cmd.Parameters.AddWithValue("@sev", entry.Severity);
        cmd.Parameters.AddWithValue("@rr", (object?)entry.RelatedRoutes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rc", (object?)entry.RelatedClusters ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at", entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AIAnalysisEntry>> GetRecentAsync(int maxCount = 20, string? analysisType = null, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        if (analysisType != null)
        {
            cmd.CommandText = """
                SELECT id, analysis_type, content, severity, related_routes, related_clusters, created_at
                FROM ai_analysis WHERE analysis_type = @type
                ORDER BY id DESC LIMIT @limit
                """;
            cmd.Parameters.AddWithValue("@type", analysisType);
        }
        else
        {
            cmd.CommandText = """
                SELECT id, analysis_type, content, severity, related_routes, related_clusters, created_at
                FROM ai_analysis ORDER BY id DESC LIMIT @limit
                """;
        }
        cmd.Parameters.AddWithValue("@limit", maxCount);

        var results = new List<AIAnalysisEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadEntry(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<AIAnalysisEntry>> GetBySeverityAsync(int minSeverity, int maxCount = 20, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, analysis_type, content, severity, related_routes, related_clusters, created_at
            FROM ai_analysis WHERE severity >= @sev
            ORDER BY severity DESC, id DESC LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@sev", minSeverity);
        cmd.Parameters.AddWithValue("@limit", maxCount);

        var results = new List<AIAnalysisEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadEntry(reader));
        }
        return results;
    }

    public async Task PurgeOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ai_analysis WHERE created_at < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteByIdAsync(long id, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ai_analysis WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static AIAnalysisEntry ReadEntry(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        AnalysisType = r.GetString(1),
        Content = r.GetString(2),
        Severity = r.GetInt32(3),
        RelatedRoutes = r.IsDBNull(4) ? null : r.GetString(4),
        RelatedClusters = r.IsDBNull(5) ? null : r.GetString(5),
        CreatedAt = DateTime.Parse(r.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };
}
