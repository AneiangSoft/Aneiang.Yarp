using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// High-performance structured logging service using Channel for async processing.
/// Features:
/// - Lock-free log ingestion via Channel
/// - Background batch processing
/// - Tiered storage: Memory -> SQLite -> File Archive
/// - SSE streaming support for real-time log delivery
/// </summary>
public class StructuredLogService : IHostedService, IDisposable
{
    // Channel for lock-free log ingestion
    private readonly Channel<LogEntry> _logChannel;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;

    // In-memory ring buffer for hot data
    private readonly ProxyLogStore _ringBuffer;

    // Batch processing configuration
    private const int BatchSize = 100;
    private const int MaxQueueSize = 10000;
    private static readonly TimeSpan BatchFlushInterval = TimeSpan.FromSeconds(1);

    // SSE subscribers for real-time streaming
    private readonly List<Channel<LogEntry>> _sseSubscribers = new();
    private readonly object _subscriberLock = new();

    // Tiered storage
    private readonly string? _archivePath;
    private readonly bool _enablePersistence;
    private readonly string? _sqliteConnectionString;
    private readonly SemaphoreSlim _persistLock = new(1, 1);
    private bool _persistenceInitialized;

    // File archive rotation
    private readonly int _maxArchiveFileSizeMb;
    private readonly int _maxArchiveDays;
    private string? _currentArchiveFile;
    private long _currentArchiveSize;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public StructuredLogService(
        ProxyLogStore ringBuffer,
        IConfiguration? configuration = null)
    {
        _ringBuffer = ringBuffer;

        // Bounded channel to prevent memory explosion under high load
        _logChannel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(MaxQueueSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // Drop oldest logs when full
            SingleReader = true,
            SingleWriter = false
        });

        // Optional persistence configuration
        _enablePersistence = configuration?.GetValue<bool>("Dashboard:EnableLogPersistence") ?? false;
        if (_enablePersistence)
        {
            _archivePath = configuration?.GetValue<string>("Dashboard:LogArchivePath") ?? "logs/archive";
            Directory.CreateDirectory(_archivePath);

            // SQLite for searchable warm storage
            var dbPath = Path.Combine(_archivePath, "logs.db");
            _sqliteConnectionString = $"Data Source={dbPath}";

            // Archive rotation settings
            _maxArchiveFileSizeMb = configuration?.GetValue<int>("Dashboard:MaxArchiveFileSizeMb") ?? 100;
            _maxArchiveDays = configuration?.GetValue<int>("Dashboard:MaxArchiveDays") ?? 7;
        }
    }

    /// <summary>
    /// Enqueues a log entry for async processing. Non-blocking, lock-free.
    /// </summary>
    public ValueTask EnqueueAsync(LogEntry entry)
    {
        // Fast path: try write without async
        if (_logChannel.Writer.TryWrite(entry))
        {
            return ValueTask.CompletedTask;
        }

        // Slow path: async write (should rarely happen with bounded channel)
        return _logChannel.Writer.WriteAsync(entry);
    }

    /// <summary>
    /// Subscribes to real-time log stream via SSE.
    /// </summary>
    public Channel<LogEntry> SubscribeToStream()
    {
        var channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        lock (_subscriberLock)
        {
            _sseSubscribers.Add(channel);
        }

        return channel;
    }

    /// <summary>
    /// Unsubscribes from real-time log stream.
    /// </summary>
    public void UnsubscribeFromStream(Channel<LogEntry> channel)
    {
        lock (_subscriberLock)
        {
            _sseSubscribers.Remove(channel);
        }
        channel.Writer.Complete();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _processingTask = ProcessLogsAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _logChannel.Writer.Complete();

        if (_processingTask != null)
        {
            await _processingTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    /// <summary>
    /// Background log processing loop.
    /// Batches logs and writes to multiple storage tiers.
    /// </summary>
    private async Task ProcessLogsAsync(CancellationToken ct)
    {
        var batch = new List<LogEntry>(BatchSize);
        var flushTimer = new PeriodicTimer(BatchFlushInterval);

        try
        {
            await foreach (var entry in _logChannel.Reader.ReadAllAsync(ct))
            {
                batch.Add(entry);

                // Process batch when full or timer fires
                if (batch.Count >= BatchSize)
                {
                    await ProcessBatchAsync(batch, ct);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            // Log to console as last resort
            Console.Error.WriteLine($"[StructuredLogService] Processing error: {ex}");
        }
        finally
        {
            // Final flush
            if (batch.Count > 0)
            {
                await ProcessBatchAsync(batch, ct);
            }
            flushTimer.Dispose();
        }
    }

    /// <summary>
    /// Processes a batch of logs: ring buffer, SSE broadcast, persistence.
    /// </summary>
    private async Task ProcessBatchAsync(List<LogEntry> batch, CancellationToken ct)
    {
        try
        {
            // 1. Write to in-memory ring buffer (for API queries)
            foreach (var entry in batch)
            {
                _ringBuffer.Add(entry);
            }

            // 2. Broadcast to SSE subscribers (real-time streaming)
            await BroadcastToSubscribersAsync(batch, ct);

            // 3. Persist to warm storage (SQLite) and cold storage (files)
            if (_enablePersistence)
            {
                await PersistBatchAsync(batch, ct);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[StructuredLogService] Batch processing error: {ex}");
        }
    }

    /// <summary>
    /// Broadcasts logs to all SSE subscribers.
    /// </summary>
    private async Task BroadcastToSubscribersAsync(List<LogEntry> batch, CancellationToken ct)
    {
        List<Channel<LogEntry>>? subscribers = null;

        lock (_subscriberLock)
        {
            if (_sseSubscribers.Count > 0)
            {
                subscribers = _sseSubscribers.ToList();
            }
        }

        if (subscribers == null) return;

        foreach (var entry in batch)
        {
            var deadSubscribers = new List<Channel<LogEntry>>();

            foreach (var sub in subscribers)
            {
                try
                {
                    if (!sub.Writer.TryWrite(entry))
                    {
                        deadSubscribers.Add(sub);
                    }
                }
                catch
                {
                    deadSubscribers.Add(sub);
                }
            }

            // Clean up dead subscribers
            if (deadSubscribers.Count > 0)
            {
                lock (_subscriberLock)
                {
                    foreach (var dead in deadSubscribers)
                    {
                        _sseSubscribers.Remove(dead);
                        dead.Writer.Complete();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Persists batch to SQLite and file archives with rotation support.
    /// </summary>
    private async Task PersistBatchAsync(List<LogEntry> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return;

        await _persistLock.WaitAsync(ct);
        try
        {
            // 1. Initialize persistence if needed
            if (!_persistenceInitialized)
            {
                await InitializePersistenceAsync(ct);
                _persistenceInitialized = true;
            }

            // 2. Clean up old archives (once per hour is enough, check by file age)
            await CleanupOldArchivesAsync(ct);

            // 3. Insert to SQLite
            await InsertToSqliteAsync(batch, ct);

            // 4. Append to rolling file archive
            await AppendToFileArchiveAsync(batch, ct);
        }
        catch (Exception ex)
        {
            // Log but don't throw - persistence failure shouldn't break the gateway
            Console.Error.WriteLine($"[StructuredLogService] Persistence error: {ex.Message}");
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private async Task InitializePersistenceAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_sqliteConnectionString)) return;

        await using var conn = new SqliteConnection(_sqliteConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS logs (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp  TEXT NOT NULL,
                event_type TEXT NOT NULL,
                level      TEXT NOT NULL,
                category   TEXT NOT NULL,
                message    TEXT,
                trace_id   TEXT,
                route_id   TEXT,
                cluster_id TEXT,
                method     TEXT,
                upstream_path TEXT,
                downstream_url TEXT,
                status_code INTEGER,
                elapsed_ms REAL,
                request_body TEXT,
                response_body TEXT,
                exception  TEXT,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS ix_logs_timestamp ON logs(timestamp);
            CREATE INDEX IF NOT EXISTS ix_logs_trace_id ON logs(trace_id);
            CREATE INDEX IF NOT EXISTS ix_logs_route_id ON logs(route_id);
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        Console.WriteLine("[StructuredLogService] SQLite persistence initialized");
    }

    private async Task InsertToSqliteAsync(List<LogEntry> batch, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_sqliteConnectionString)) return;

        await using var conn = new SqliteConnection(_sqliteConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO logs (timestamp, event_type, level, category, message,
                trace_id, route_id, cluster_id, method, upstream_path,
                downstream_url, status_code, elapsed_ms,
                request_body, response_body, exception)
            VALUES (@ts, @type, @level, @cat, @msg,
                @traceId, @routeId, @clusterId, @method, @upstreamPath,
                @downstreamUrl, @statusCode, @elapsedMs,
                @requestBody, @responseBody, @exception)
            """;

        foreach (var entry in batch)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@ts", entry.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@type", entry.EventType.ToString());
            cmd.Parameters.AddWithValue("@level", entry.Level ?? string.Empty);
            cmd.Parameters.AddWithValue("@cat", entry.Category ?? string.Empty);
            cmd.Parameters.AddWithValue("@msg", entry.Message ?? string.Empty);
            cmd.Parameters.AddWithValue("@traceId", entry.TraceId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@routeId", entry.RouteId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@clusterId", entry.ClusterId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@method", entry.Method ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@upstreamPath", entry.UpstreamPath ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@downstreamUrl", entry.DownstreamUrl ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@statusCode", entry.StatusCode ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@elapsedMs", entry.ElapsedMs ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@requestBody", entry.RequestBody ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@responseBody", entry.ResponseBody ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@exception", entry.Exception ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task AppendToFileArchiveAsync(List<LogEntry> batch, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_archivePath)) return;

        // Determine current archive file (one per day)
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var archiveFile = Path.Combine(_archivePath, $"logs-{today}.jsonl");

        // Check if we need to rotate (file size limit)
        if (_currentArchiveFile != archiveFile)
        {
            _currentArchiveFile = archiveFile;
            _currentArchiveSize = File.Exists(archiveFile) ? new FileInfo(archiveFile).Length : 0;
        }

        var maxFileSize = (long)_maxArchiveFileSizeMb * 1024 * 1024;
        if (_currentArchiveSize >= maxFileSize)
        {
            // Rotate to new file with timestamp suffix
            var timestamp = DateTime.UtcNow.ToString("HHmmss");
            archiveFile = Path.Combine(_archivePath, $"logs-{today}-{timestamp}.jsonl");
            _currentArchiveFile = archiveFile;
            _currentArchiveSize = 0;
        }

        // Append entries as JSON Lines
        var lines = batch.Select(e => JsonSerializer.Serialize(e, _jsonOptions));
        var content = string.Join('\n', lines) + '\n';
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);

        await File.AppendAllTextAsync(archiveFile, content, ct);
        _currentArchiveSize += bytes.Length;
    }

    private DateTime _lastCleanupTime = DateTime.MinValue;

    private async Task CleanupOldArchivesAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_archivePath)) return;

        // Only cleanup once per hour
        if ((DateTime.UtcNow - _lastCleanupTime).TotalHours < 1) return;
        _lastCleanupTime = DateTime.UtcNow;

        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-_maxArchiveDays);
            var cutoffDateStr = cutoffDate.ToString("yyyy-MM-dd");

            foreach (var file in Directory.EnumerateFiles(_archivePath, "logs-*.jsonl"))
            {
                var fileName = Path.GetFileName(file);
                // Extract date from filename (logs-YYYY-MM-DD*.jsonl)
                if (fileName.Length >= 15)
                {
                    var dateStr = fileName.Substring(5, 10);
                    if (string.Compare(dateStr, cutoffDateStr, StringComparison.Ordinal) < 0)
                    {
                        await Task.Run(() => File.Delete(file), ct);
                    }
                }
            }

            // Also cleanup old SQLite WAL files
            if (File.Exists(Path.Combine(_archivePath, "logs.db")))
            {
                // SQLite will handle WAL cleanup automatically
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[StructuredLogService] Cleanup error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _logChannel.Writer.Complete();
        _persistLock.Dispose();
    }
}
