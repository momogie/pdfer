using System.Text;

namespace PdfEr.Infrastructure.PdfWriters;

public sealed class PdfBuffer
{
    private readonly List<byte[]> _chunks = new();
    private int _totalLength;

    public int Length => _totalLength;

    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var bytes = Encoding.ASCII.GetBytes(text);
        _chunks.Add(bytes);
        _totalLength += bytes.Length;
    }

    public void Append(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        _chunks.Add(data);
        _totalLength += data.Length;
    }

    public void AppendLine(string text = "")
    {
        Append(text);
        Append("\n");
    }

    public void AppendFormat(string format, params object[] args)
    {
        Append(string.Format(format, args));
    }

    public byte[] ToByteArray()
    {
        var result = new byte[_totalLength];
        var offset = 0;
        foreach (var chunk in _chunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }
        return result;
    }

    public void WriteToStream(Stream stream)
    {
        foreach (var chunk in _chunks)
            stream.Write(chunk, 0, chunk.Length);
    }

    public async Task WriteToStreamAsync(Stream stream, CancellationToken ct = default)
    {
        foreach (var chunk in _chunks)
            await stream.WriteAsync(chunk, 0, chunk.Length, ct);
    }

    public void Clear()
    {
        _chunks.Clear();
        _totalLength = 0;
    }
}
