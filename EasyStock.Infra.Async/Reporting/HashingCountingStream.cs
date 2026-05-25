using System.Security.Cryptography;

namespace EasyStock.Infra.Async.Reporting;

/// <summary>
/// Stream decorador que calcula SHA-256 e conta bytes escritos enquanto passa dados adiante.
/// Usado no pipeline de geração de relatório para commit atômico de integridade + tamanho.
/// </summary>
public sealed class HashingCountingStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _sha256;
    private long _bytesWritten;
    private bool _disposed;

    public HashingCountingStream(Stream inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    }

    public long BytesWritten => _bytesWritten;

    /// <summary>
    /// Retorna o hash SHA-256 hexadecimal (lowercase, 64 chars) dos dados escritos até agora.
    /// Pode ser chamado múltiplas vezes — retorna o hash acumulado até o momento.
    /// </summary>
    public string GetHexHash()
    {
        var hash = _sha256.GetCurrentHash();
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── Stream overrides necessários ─────────────────────────────────────────

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _sha256.AppendData(buffer, offset, count);
        _inner.Write(buffer, offset, count);
        _bytesWritten += count;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _sha256.AppendData(buffer);
        _inner.Write(buffer);
        _bytesWritten += buffer.Length;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        _sha256.AppendData(buffer, offset, count);
        await _inner.WriteAsync(buffer, offset, count, ct);
        _bytesWritten += count;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        _sha256.AppendData(buffer.Span);
        await _inner.WriteAsync(buffer, ct);
        _bytesWritten += buffer.Length;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("HashingCountingStream é write-only.");

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _sha256.Dispose();
            _inner.Dispose();
        }
        _disposed = true;
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _sha256.Dispose();
        await _inner.DisposeAsync();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
