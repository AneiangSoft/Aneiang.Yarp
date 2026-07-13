using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>Add tool_call_id column to ai_conversations for function calling support.</summary>
internal sealed class Migration008_ToolCallId : ISchemaMigration
{
    public int Version => 8;
    public string Id => "008_tool_call_id";
    public string Description => "Add tool_call_id column to ai_conversations table";

    public Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
        => ExecuteAsync(conn, transaction, """
            ALTER TABLE ai_conversations ADD COLUMN tool_call_id TEXT;
            """, ct);
}
