using System.Net.Http.Headers;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Streams;

public sealed class HttpRangeStream(
    HttpClient httpClient,
    DirectLinkBlob link,
    long length,
    int chunkSizeBytes = 2 * 1024 * 1024
) : Stream
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly DirectLinkBlob _link = link;
    private readonly long _length = length;
    private readonly int _chunkSize = Math.Max(64 * 1024, chunkSizeBytes);

    private long _position;
    private long _bufferStart;
    private byte[] _buffer = [];
    private int _bufferLength;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (count <= 0) return 0;
        if (_position >= _length) return 0;

        if (!HasCurrentPositionBuffered())
        {
            await FillBufferAsync(_position, count, cancellationToken).ConfigureAwait(false);
            if (_bufferLength == 0) return 0;
        }

        var offsetInBuffer = (int)(_position - _bufferStart);
        var remainingInBuffer = _bufferLength - offsetInBuffer;
        var remainingInFile = (int)Math.Min(int.MaxValue, _length - _position);
        var bytesToCopy = Math.Min(count, Math.Min(remainingInBuffer, remainingInFile));

        Array.Copy(_buffer, offsetInBuffer, buffer, offset, bytesToCopy);
        _position += bytesToCopy;
        return bytesToCopy;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var next = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
        };

        if (next < 0) throw new IOException("Cannot seek before start of stream.");
        _position = next;
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    private bool HasCurrentPositionBuffered()
    {
        return _bufferLength > 0
               && _position >= _bufferStart
               && _position < _bufferStart + _bufferLength;
    }

    private async Task FillBufferAsync(long start, int requestedCount, CancellationToken ct)
    {
        var end = Math.Min(_length - 1, start + Math.Max(_chunkSize, requestedCount) - 1);
        using var request = new HttpRequestMessage(HttpMethod.Get, _link.Url);
        request.Headers.Range = new RangeHeaderValue(start, end);

        if (!string.IsNullOrWhiteSpace(_link.UserAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", _link.UserAgent);
        if (!string.IsNullOrWhiteSpace(_link.Referer)
            && Uri.TryCreate(_link.Referer, UriKind.Absolute, out var refererUri))
            request.Headers.Referrer = refererUri;
        if (!string.IsNullOrWhiteSpace(_link.Cookie))
            request.Headers.TryAddWithoutValidation("Cookie", _link.Cookie);
        foreach (var (key, value) in _link.Headers)
            request.Headers.TryAddWithoutValidation(key, value);

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        _bufferStart = start;
        _buffer = data;
        _bufferLength = data.Length;
    }
}