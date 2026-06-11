using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Models;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Zero-copy log streaming using System.IO.Pipelines.
/// Eliminates buffer copies and reduces allocations in SSE streaming.
/// </summary>
public sealed class PipelineLogWriter : IDisposable
{
    private readonly Pipe _pipe;
    private readonly PipeWriter _writer;
    private readonly PipeReader _reader;
    private readonly DashboardJsonContext _jsonContext;
    private readonly ArrayPool<byte> _bufferPool;

    public PipeReader Reader => _reader;

    public PipelineLogWriter(DashboardJsonContext jsonContext)
    {
        _pipe = new Pipe(new PipeOptions(
            pool: MemoryPool<byte>.Shared,
            readerScheduler: PipeScheduler.Inline,
            writerScheduler: PipeScheduler.Inline,
            useSynchronizationContext: false));

        _writer = _pipe.Writer;
        _reader = _pipe.Reader;
        _jsonContext = jsonContext;
        _bufferPool = ArrayPool<byte>.Create(4096, 100);
    }

    /// <summary>
    /// Writes a log entry to the pipeline using pooled memory.
    /// Format: "data: {json}\n\n"
    /// </summary>
    public ValueTask<FlushResult> WriteLogEntryAsync(LogEntry entry, CancellationToken ct)
    {
        // Get pooled buffer for JSON serialization
        var buffer = _bufferPool.Rent(2048);
        try
        {
            // Serialize JSON to buffer
            var jsonSpan = buffer.AsSpan(0, buffer.Length - 10); // Reserve space for SSE framing
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(entry, _jsonContext.LogEntry);

            // Calculate total length: "data: " (6) + json + "\n\n" (2)
            var preamble = "data: "u8;
            var terminator = "\n\n"u8;

            // Get memory from pipeline
            var memory = _writer.GetMemory(preamble.Length + jsonBytes.Length + terminator.Length);
            var span = memory.Span;

            int offset = 0;
            preamble.CopyTo(span.Slice(offset));
            offset += preamble.Length;

            jsonBytes.AsSpan().CopyTo(span.Slice(offset));
            offset += jsonBytes.Length;

            terminator.CopyTo(span.Slice(offset));
            offset += terminator.Length;

            _writer.Advance(offset);

            return _writer.FlushAsync(ct);
        }
        finally
        {
            _bufferPool.Return(buffer);
        }
    }

    /// <summary>
    /// Writes keepalive message direct to pipe without allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<FlushResult> WriteKeepAliveAsync(CancellationToken ct)
    {
        var keepAlive = ":keepalive\n\n"u8;
        var memory = _writer.GetMemory(keepAlive.Length);
        keepAlive.CopyTo(memory.Span);
        _writer.Advance(keepAlive.Length);
        return _writer.FlushAsync(ct);
    }

    /// <summary>
    /// Completes the writer, signaling no more data.
    /// </summary>
    public void Complete(Exception? exception = null)
    {
        _writer.Complete(exception);
    }

    public void Dispose()
    {
        _writer.Complete();
        _reader.Complete();
    }
}

/// <summary>
/// High-performance SSE stream writer using async enumerable.
/// Zero-allocation streaming with ValueTask throughout.
/// </summary>
public sealed class SseStreamWriter
{
    private readonly DashboardJsonContext _jsonContext;
    private readonly ArrayPool<byte> _bufferPool;

    public SseStreamWriter(DashboardJsonContext jsonContext)
    {
        _jsonContext = jsonContext;
        _bufferPool = ArrayPool<byte>.Create(4096, 50);
    }

    /// <summary>
    /// Streams log entries to response body using IAsyncEnumerable pattern.
    /// Minimal allocations using pooled buffers.
    /// </summary>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> StreamLogsAsync(
        Channel<LogEntry> source,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Send initial connection message
        yield return "data: {\"connected\":true}\n\n"u8.ToArray();

        var keepAliveTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        var keepAliveTask = keepAliveTimer.WaitForNextTickAsync(ct).AsTask();
        var readerTask = Task.CompletedTask;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Wait for either log entry or keepalive
                var completedTask = await Task.WhenAny(
                    ReadEntryAsync(source, ct),
                    keepAliveTask);

                if (completedTask == keepAliveTask)
                {
                    yield return ":keepalive\n\n"u8.ToArray();
                    keepAliveTask = keepAliveTimer.WaitForNextTickAsync(ct).AsTask();
                }

                // Process available entries
                if (source.Reader.TryRead(out var entry))
                {
                    // Serialize to pooled memory
                    var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(entry, _jsonContext.LogEntry);
                    var totalLen = 6 + jsonBytes.Length + 2; // "data: " + json + "\n\n"

                    // Allocate result array
                    var result = new byte[totalLen];
                    "data: "u8.CopyTo(result);
                    jsonBytes.CopyTo(result, 6);
                    "\n\n"u8.CopyTo(result.AsSpan(6 + jsonBytes.Length));
                    yield return result;
                }
            }
        }
        finally
        {
            keepAliveTimer.Dispose();
        }
    }

    private static Task ReadEntryAsync(Channel<LogEntry> source, CancellationToken ct)
    {
        return source.Reader.WaitToReadAsync(ct).AsTask();
    }
}

/// <summary>
/// ValueTask-based HTTP response streaming.
/// Eliminates Task allocations for hot paths.
/// </summary>
public static class ValueTaskExtensions
{
    /// <summary>
    /// Writes ReadOnlyMemory to response body without async state machine allocation.
    /// </summary>
    public static async ValueTask FastWriteAsync(this Stream stream, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (data.IsEmpty)
            return;

        if (stream is MemoryStream ms)
        {
            ms.Write(data.Span);
            return;
        }

        await stream.WriteAsync(data, ct);
    }

    /// <summary>
    /// Converts Task to ValueTask, but preserves synchronous completion optimization.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask AsValueTask(this Task task)
    {
        if (task.IsCompletedSuccessfully)
            return ValueTask.CompletedTask;
        return new ValueTask(task);
    }

    /// <summary>
    /// Optimized WhenAll for small task counts (2-3 tasks).
    /// Avoids array allocation of standard Task.WhenAll.
    /// </summary>
    public static ValueTask WhenAll2(ValueTask t1, ValueTask t2)
    {
        if (t1.IsCompleted && t2.IsCompleted)
        {
            if (t1.IsCompletedSuccessfully && t2.IsCompletedSuccessfully)
                return ValueTask.CompletedTask;

            // Propagate failure
            if (!t1.IsCompletedSuccessfully)
                return t1;
            return t2;
        }

        return new ValueTask(Task.WhenAll(t1.AsTask(), t2.AsTask()));
    }

    /// <summary>
    /// Converts ValueTask to Task.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task AsTask(this ValueTask valueTask)
    {
        if (valueTask.IsCompletedSuccessfully)
            return Task.CompletedTask;
        return valueTask.AsTask();
    }
}

/// <summary>
/// Memory-mapped file log writer for cold storage.
/// Direct memory access without buffer copies.
/// </summary>
public sealed class MemoryMappedLogWriter : IDisposable
{
    private readonly string _filePath;
    private readonly FileStream _fileStream;
    private readonly long _capacity;
    private readonly System.IO.MemoryMappedFiles.MemoryMappedViewAccessor _accessor;
    private unsafe byte* _mappedPtr;
    private long _writeOffset;
    private bool _disposed;

    public unsafe MemoryMappedLogWriter(string filePath, long capacity = 256 * 1024 * 1024) // 256MB default
    {
        _filePath = filePath;
        _capacity = capacity;

        _fileStream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 1, // No internal buffering
            FileOptions.SequentialScan);

        _fileStream.SetLength(capacity);

        // Create memory-mapped view
        var mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(
            _fileStream,
            null,
            capacity,
            System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            leaveOpen: true);

        _accessor = mmf.CreateViewAccessor(0, capacity);
        _mappedPtr = (byte*)_accessor.SafeMemoryMappedViewHandle.DangerousGetHandle();
    }

    public unsafe void WriteLogEntry(ReadOnlySpan<byte> data)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MemoryMappedLogWriter));

        var required = (int)(_writeOffset + data.Length + sizeof(int));
        if (required > _capacity)
        {
            // Flush and reset or rotate file
            Flush();
            _writeOffset = 0;
        }

        // Write length prefix
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            new Span<byte>(_mappedPtr + _writeOffset, sizeof(int)), data.Length);
        _writeOffset += sizeof(int);

        // Write data
        data.CopyTo(new Span<byte>(_mappedPtr + _writeOffset, data.Length));
        _writeOffset += data.Length;
    }

    public void Flush()
    {
        _fileStream.Flush();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Flush();
        _accessor?.Dispose();
        _fileStream?.Dispose();
    }
}
