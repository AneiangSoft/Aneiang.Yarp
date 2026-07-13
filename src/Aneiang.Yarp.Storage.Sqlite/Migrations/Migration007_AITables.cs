using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>AI conversation history and analysis tables.</summary>
internal sealed class Migration007_AITables : ISchemaMigration
{
    public int Version => 7;
    public string Id => "007_ai_tables";
    public string Description => "Create AI conversation and analysis tables";

    public Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
        => ExecuteAsync(conn, transaction, """
            CREATE TABLE IF NOT EXISTS ai_conversations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                function_calls TEXT,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ai_analysis (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                analysis_type TEXT NOT NULL,
                content TEXT NOT NULL,
                severity INTEGER DEFAULT 0,
                related_routes TEXT,
                related_clusters TEXT,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_ai_conv_session ON ai_conversations(session_id);
            CREATE INDEX IF NOT EXISTS ix_ai_conv_created ON ai_conversations(created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_ai_analysis_type ON ai_analysis(analysis_type);
            CREATE INDEX IF NOT EXISTS ix_ai_analysis_created ON ai_analysis(created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_ai_analysis_severity ON ai_analysis(severity DESC);
            """, ct);
}
