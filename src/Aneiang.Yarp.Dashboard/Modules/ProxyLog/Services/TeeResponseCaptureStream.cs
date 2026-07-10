using Microsoft.IO;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// TeeStream captures response body while forwarding to the original response stream.
/// Uses RecyclableMemoryStreamManager to eliminate LOH fragmentation.
/// </summary>
public sealed class TeeResponseCaptureStream : Stream
{
    private readonly Stream _inner;
    private readonly int _limitBytes;
    private int _capturedBytes;

    public TeeResponseCaptureStream(Stream inner, int limitBytes, RecyclableMemoryStreamManager manager)
    {
        _inner = inner;
        _limitBytes = Math.Max(0, limitBytes);
        CapturedBody = manager.GetStream("TeeResponseCapture", Math.Min(_limitBytes, 64 * 1024));
    }

    public Stream CapturedBody { get; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        Capture(buffer.AsSpan(offset, count));
        _inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Capture(buffer);
        _inner.Write(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Capture(buffer.Span);
        await _inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Capture(buffer.AsSpan(offset, count));
        return _inner.WriteAsync(buffer, offset, count, cancellationToken);
    }

    private void Capture(ReadOnlySpan<byte> buffer)
    {
        if (_capturedBytes >= _limitBytes || buffer.Length == 0)
            return;

        var remaining = _limitBytes - _capturedBytes;
        var bytesToCapture = Math.Min(remaining, buffer.Length);
        CapturedBody.Write(buffer[..bytesToCapture]);
        _capturedBytes += bytesToCapture;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            CapturedBody.Dispose();
        base.Dispose(disposing);
    }
}
