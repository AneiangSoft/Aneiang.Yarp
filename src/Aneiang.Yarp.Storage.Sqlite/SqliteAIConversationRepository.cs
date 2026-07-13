using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>SQLite implementation of AI conversation history persistence.</summary>
public class SqliteAIConversationRepository : IAIConversationRepository
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteAIConversationRepository(SqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task SaveMessageAsync(AIConversationEntry entry, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ai_conversations (session_id, role, content, function_calls, tool_call_id, created_at)
            VALUES (@sid, @role, @content, @fc, @tci, @at)
            """;
        cmd.Parameters.AddWithValue("@sid", entry.SessionId);
        cmd.Parameters.AddWithValue("@role", entry.Role);
        cmd.Parameters.AddWithValue("@content", entry.Content);
        cmd.Parameters.AddWithValue("@fc", (object?)entry.FunctionCalls ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tci", (object?)entry.ToolCallId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@at", entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AIConversationEntry>> GetSessionMessagesAsync(string sessionId, int maxCount = 20, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, session_id, role, content, function_calls, tool_call_id, created_at
            FROM ai_conversations
            WHERE session_id = @sid
            ORDER BY id DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.Parameters.AddWithValue("@limit", maxCount);

        var results = new List<AIConversationEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new AIConversationEntry
            {
                Id = reader.GetInt64(0),
                SessionId = reader.GetString(1),
                Role = reader.GetString(2),
                Content = reader.GetString(3),
                FunctionCalls = reader.IsDBNull(4) ? null : reader.GetString(4),
                ToolCallId = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }
        results.Reverse(); // Return chronological order
        return results;
    }

    public async Task<IReadOnlyList<AISessionSummary>> ListSessionsAsync(int maxCount = 50, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, COUNT(*) as cnt, MAX(created_at) as last_at
            FROM ai_conversations
            GROUP BY session_id
            ORDER BY last_at DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@limit", maxCount);

        var results = new List<AISessionSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new AISessionSummary
            {
                SessionId = reader.GetString(0),
                MessageCount = reader.GetInt32(1),
                LastActivity = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }
        return results;
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ai_conversations WHERE session_id = @sid";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task PurgeOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ai_conversations WHERE created_at < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
