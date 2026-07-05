using System.IO.Compression;
using System.Text;
using PdfEr.Core.Domain.Enums;

namespace PdfEr.Infrastructure.PdfWriters;

public sealed class PdfStreamWriter : IDisposable
{
    private readonly Stream _output;
    private readonly StreamWriter _writer;
    private int _nextObjectNumber = 1;
    private readonly List<long> _objectOffsets = new();
    private long _currentOffset;
    private bool _disposed;
    private int _pagesRootRef;
    private readonly List<int> _pageRefs = new();

    public PdfStreamWriter(Stream output)
    {
        _output = output;
        _writer = new StreamWriter(output, Encoding.ASCII, 4096, leaveOpen: true);
    }

    private int AllocateObjectNumber() => _nextObjectNumber++;

    public void WriteHeader(PdfVersion version = PdfVersion.V1_7)
    {
        var ver = version switch
        {
            PdfVersion.V1_0 => "1.0", PdfVersion.V1_1 => "1.1", PdfVersion.V1_2 => "1.2",
            PdfVersion.V1_3 => "1.3", PdfVersion.V1_4 => "1.4", PdfVersion.V1_5 => "1.5",
            PdfVersion.V1_6 => "1.6", PdfVersion.V1_7 => "1.7", PdfVersion.V2_0 => "2.0",
            _ => "1.7"
        };
        WriteLine($"%PDF-{ver}");
        WriteLine("%\u00e2\u00e3\u00cf\u00d3");
    }

    public int BeginObject()
    {
        RecordOffset();
        var num = AllocateObjectNumber();
        WriteLine($"{num} 0 obj");
        return num;
    }

    public void EndObject()
    {
        WriteLine("endobj");
    }

    public int WriteStreamObject(byte[] streamData)
    {
        var num = BeginObject();
        WriteLine($"<< /Length {streamData.Length} >>");
        WriteLine("stream");
        _writer.Flush();
        _output.Write(streamData, 0, streamData.Length);
        _output.WriteByte((byte)'\n');
        EndObject();
        return num;
    }

    public int WriteCompressedStreamObject(byte[] data)
    {
        var compressed = DeflateCompressStream(data);
        var num = BeginObject();
        WriteLine($"<< /Length {compressed.Length} /Filter /FlateDecode >>");
        WriteLine("stream");
        _writer.Flush();
        _output.Write(compressed, 0, compressed.Length);
        _output.WriteByte((byte)'\n');
        EndObject();
        return num;
    }

    public int WritePagesRoot()
    {
        _pagesRootRef = BeginObject();
        WriteLine("<< /Type /Pages");
        WriteLine("   /Kids []");
        WriteLine("   /Count 0");
        WriteLine(">>");
        EndObject();
        return _pagesRootRef;
    }

    public int WritePage(int parentRef, string textContent)
    {
        var num = BeginObject();
        var contentNum = WriteStreamObject(Encoding.ASCII.GetBytes(
            $"BT\n/F1 12 Tf\n72 800 Td\n({EscapePdfString(textContent)}) Tj\nET\n"));

        WriteLine("<< /Type /Page");
        WriteLine($"   /Parent {parentRef} 0 R");
        WriteLine("   /MediaBox [0 0 595.28 841.89]");
        WriteLine($"   /Contents {contentNum} 0 R");
        WriteLine("   /Resources << /Font << /F1 5 0 R >> >>");
        WriteLine(">>");
        EndObject();

        _pageRefs.Add(num);
        return num;
    }

    public async Task WriteToFileAsync(string filePath, CancellationToken ct = default)
    {
        long xrefOffset = _currentOffset;

        WriteLine("xref");
        WriteLine($"0 {_nextObjectNumber}");
        WriteLine("0000000000 65535 f ");
        foreach (var offset in _objectOffsets)
            WriteLine($"{offset:D10} 00000 n ");

        WriteLine("trailer");
        WriteLine("<< /Size " + _nextObjectNumber + " /Root 4 0 R /Info 3 0 R >>");
        WriteLine("startxref");
        WriteLine(xrefOffset.ToString());
        await _writer.FlushAsync();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer.Flush();
            _writer.Dispose();
            if (_output is FileStream fs) fs.Dispose();
            _disposed = true;
        }
    }

    private void WriteLine(string text)
    {
        _writer.WriteLine(text);
        _currentOffset += Encoding.ASCII.GetByteCount(text) + 1;
    }

    private void RecordOffset()
    {
        _objectOffsets.Add(_currentOffset);
    }

    private static string EscapePdfString(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    private static byte[] DeflateCompressStream(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal))
            deflate.Write(data, 0, data.Length);
        return output.ToArray();
    }
}
