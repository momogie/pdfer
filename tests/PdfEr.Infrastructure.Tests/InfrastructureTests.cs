using Microsoft.Extensions.Logging.Abstractions;
using PdfEr.Infrastructure.Utilities;
using PdfEr.Infrastructure.Caching;
using PdfEr.Infrastructure.PdfWriters;
using PdfEr.Infrastructure.Typography;
using PdfEr.Core.Domain.Forms;
using PdfEr.Core.Domain.Toc;
using PdfEr.Core.Domain.Typography;

namespace PdfEr.Infrastructure.Tests;

public class UnitConverterTests
{
    private readonly UnitConverter _converter = new();

    [Fact]
    public void ConvertMillimetersToPoints_Correct()
    {
        var result = _converter.ConvertMillimetersToPoints(25.4f);
        Assert.Equal(72f, result, 1);
    }

    [Fact]
    public void ConvertPointsToMillimeters_Correct()
    {
        var result = _converter.ConvertPointsToMillimeters(72);
        Assert.Equal(25.4f, result, 1);
    }

    [Fact]
    public void ConvertToPoints_Pt_ReturnsSame()
    {
        var result = _converter.ConvertToPoints(12f, Core.Domain.Enums.UnitOfMeasure.Point);
        Assert.Equal(12f, result, 0);
    }

    [Fact]
    public void ConvertToPoints_Px_Converts()
    {
        var result = _converter.ConvertPixelsToPoints(16, 96);
        Assert.Equal(12f, result, 1);
    }

    [Fact]
    public void ConvertToPoints_Mm_Converts()
    {
        var result = _converter.ConvertToPoints(25.4f, Core.Domain.Enums.UnitOfMeasure.Millimeter);
        Assert.Equal(72f, result, 1);
    }

    [Fact]
    public void ConvertToPoints_In_Converts()
    {
        var result = _converter.ConvertToPoints(1f, Core.Domain.Enums.UnitOfMeasure.Inch);
        Assert.Equal(72f, result, 0);
    }

    [Fact]
    public void ConvertToPoints_Cm_Converts()
    {
        var result = _converter.ConvertToPoints(2.54f, Core.Domain.Enums.UnitOfMeasure.Centimeter);
        Assert.Equal(72f, result, 1);
    }
}

public class FileCacheServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileCacheService _cache;
    private bool _disposed;

    public FileCacheServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "PdfErTest_" + Guid.NewGuid().ToString("N"));
        _cache = new FileCacheService(_testDir, new NullLogger<FileCacheService>(), 3600);
    }

    [Fact]
    public void TryGetValue_NonExisting_ReturnsFalse()
    {
        Assert.False(_cache.TryGetValue("nonexistent", out string? _));
    }

    [Fact]
    public void SetValue_And_TryGetValue_ReturnsData()
    {
        _cache.SetValue("key1", "hello world");
        Assert.True(_cache.TryGetValue("key1", out string? val));
        Assert.Equal("hello world", val);
    }

    [Fact]
    public void Remove_Existing_Removes()
    {
        _cache.SetValue("removekey", "data");
        _cache.Remove("removekey");
        Assert.False(_cache.TryGetValue("removekey", out string? _));
    }

    [Fact]
    public void SetValue_OverwritesExisting()
    {
        _cache.SetValue("overwrite", "first");
        _cache.SetValue("overwrite", "second");
        Assert.True(_cache.TryGetValue("overwrite", out string? val));
        Assert.Equal("second", val);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cache.Dispose();
            if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
            _disposed = true;
        }
    }
}

public class PdfBufferTests
{
    [Fact]
    public void Append_AccumulatesBytes()
    {
        var buf = new PdfBuffer();
        buf.Append("Hello");
        buf.Append(" World");
        var result = buf.ToByteArray();
        Assert.Equal(11, result.Length);
        Assert.Equal("Hello World", System.Text.Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void AppendLine_AddsNewline()
    {
        var buf = new PdfBuffer();
        buf.AppendLine("Test");
        var result = buf.ToByteArray();
        Assert.Equal("Test\n", System.Text.Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void Clear_Resets()
    {
        var buf = new PdfBuffer();
        buf.Append("Hello");
        buf.Clear();
        Assert.Equal(0, buf.Length);
    }

    [Fact]
    public void WriteToStream_WritesAllData()
    {
        var buf = new PdfBuffer();
        buf.Append("StreamTest");
        using var ms = new MemoryStream();
        buf.WriteToStream(ms);
        Assert.Equal("StreamTest", System.Text.Encoding.ASCII.GetString(ms.ToArray()));
    }

    [Fact]
    public void AppendLine_Empty_AddsNewlineOnly()
    {
        var buf = new PdfBuffer();
        buf.AppendLine();
        Assert.Equal(1, buf.Length);
        Assert.Equal("\n", System.Text.Encoding.ASCII.GetString(buf.ToByteArray()));
    }

    [Fact]
    public void AppendFormat_FormatsCorrectly()
    {
        var buf = new PdfBuffer();
        buf.AppendFormat("{0} {1}", 42, "test");
        Assert.Equal("42 test", System.Text.Encoding.ASCII.GetString(buf.ToByteArray()));
    }
}

public class PdfWriterTests
{
    [Fact]
    public void WriteSimpleDocument_ReturnsValidPdfHeader()
    {
        var writer = new PdfWriter();
        var result = writer.WriteSimpleDocument("Test", "Hello PDF");
        var header = System.Text.Encoding.ASCII.GetString(result, 0, 8);
        Assert.Equal("%PDF-1.7", header);
    }

    [Fact]
    public void WriteSimpleDocument_ContainsEOF()
    {
        var writer = new PdfWriter();
        var result = writer.WriteSimpleDocument("Test", "Content");
        var text = System.Text.Encoding.ASCII.GetString(result);
        Assert.Contains("%%EOF", text);
    }

    [Fact]
    public void WriteSimpleDocument_ContainsExpectedObjects()
    {
        var writer = new PdfWriter();
        var result = writer.WriteSimpleDocument("Title", "Content");
        var text = System.Text.Encoding.ASCII.GetString(result);
        Assert.Contains("/Type /Catalog", text);
        Assert.Contains("/Type /Pages", text);
        Assert.Contains("/Type /Page", text);
    }

    [Fact]
    public void WriteDocument_WithMetadata_IncludesInfo()
    {
        var writer = new PdfWriter();
        var metadata = new PdfMetadata
        {
            Title = "MyDoc",
            Author = "Author1",
            Subject = "Test Subject",
            Keywords = "test, pdf"
        };
        var result = writer.WriteDocument("MyDoc", "Content", metadata: metadata);
        var text = System.Text.Encoding.ASCII.GetString(result);
        Assert.Contains("MyDoc", text);
        Assert.Contains("Author1", text);
        Assert.Contains("PdfEr", text);
    }

    [Fact]
    public void WriteDocument_WithForm_IncludesAcroform()
    {
        var writer = new PdfWriter();
        var form = new FormDefinition();
        form.Fields.Add(new FormField
        {
            Name = "testField",
            Type = FormFieldType.Text,
            X = 100,
            Y = 100,
            Width = 200,
            Height = 20
        });

        var result = writer.WriteDocument("FormTest", "Content", form: form);
        var text = System.Text.Encoding.ASCII.GetString(result);
        Assert.Contains("AcroForm", text);
        Assert.Contains("testField", text);
        Assert.Contains("/Tx", text);
    }

    [Fact]
    public void WriteDocument_WithBookmarks_IncludesOutlines()
    {
        var writer = new PdfWriter();
        var bookmarks = new List<BookmarkNode>
        {
            new() { Title = "Chapter 1", PageNumber = 1, PageY = 800 }
        };
        var result = writer.WriteDocument("Bookmarks", "Content", bookmarks: bookmarks);
        var text = System.Text.Encoding.ASCII.GetString(result);
        Assert.Contains("Outlines", text);
        Assert.Contains("Chapter 1", text);
    }

    [Fact]
    public void WriteDocument_WithEncryption_IncludesEncryptDict()
    {
        var writer = new PdfWriter();
        var enc = new PdfEncryptionOptions
        {
            Encrypt = true,
            UserPassword = "user",
            OwnerPassword = "owner",
            Level = PdfEr.Infrastructure.PdfWriters.EncryptionLevel.Aes128
        };
        var result = writer.WriteDocument("Encrypted", "Secret", encryption: enc);
        var text = System.Text.Encoding.ASCII.GetString(result);
        Assert.Contains("/Filter /Standard", text);
        Assert.Contains("/CFM /AESV2", text);
    }

    [Fact]
    public void WriteEmbeddedFont_WritesFontDict()
    {
        var writer = new PdfWriter();
        var fontNum = writer.WriteEmbeddedFont("MyFont", new byte[] { 0, 1, 2, 3, 4 });
        Assert.True(fontNum > 0);
    }

    [Fact]
    public void WriteImage_WritesXObject()
    {
        var writer = new PdfWriter();
        var imgNum = writer.WriteImage("test.png", new byte[] { 255, 0, 0 }, 10, 10);
        Assert.True(imgNum > 0);
    }
}

public class FontRegistryTests
{
    [Fact]
    public void FindFont_BuiltinHelvetica_ReturnsFont()
    {
        var registry = new FontRegistry(new NullLogger<FontRegistry>());
        var font = registry.FindFont("Helvetica", FontStyle.Regular);
        Assert.NotNull(font);
        Assert.Equal("Helvetica", font.FamilyName);
    }

    [Fact]
    public void FindFont_BuiltinTimes_ReturnsFont()
    {
        var registry = new FontRegistry(new NullLogger<FontRegistry>());
        var font = registry.FindFont("Times", FontStyle.Regular);
        Assert.NotNull(font);
        Assert.Equal("Times", font.FamilyName);
    }

    [Fact]
    public void FindFont_BuiltinCourier_ReturnsFont()
    {
        var registry = new FontRegistry(new NullLogger<FontRegistry>());
        var font = registry.FindFont("Courier", FontStyle.Regular);
        Assert.NotNull(font);
        Assert.Equal("Courier", font.FamilyName);
    }

    [Fact]
    public void FindFontWithFallback_GenericSerif_ReturnsNonNull()
    {
        var registry = new FontRegistry(new NullLogger<FontRegistry>());
        var font = registry.FindFontWithFallback("serif", FontStyle.Regular);
        Assert.NotNull(font);
    }

    [Fact]
    public void FindFontWithFallback_GenericSansSerif_ReturnsNonNull()
    {
        var registry = new FontRegistry(new NullLogger<FontRegistry>());
        var font = registry.FindFontWithFallback("sans-serif", FontStyle.Regular);
        Assert.NotNull(font);
    }

    [Fact]
    public void FindFontWithFallback_UnknownFont_ReturnsNonNull()
    {
        var registry = new FontRegistry(new NullLogger<FontRegistry>());
        var font = registry.FindFontWithFallback("NonExistentFont", FontStyle.Regular);
        Assert.NotNull(font);
    }

    [Fact]
    public void RegisterFont_AddsCustomFont()
    {
        var registry = new FontRegistry(new NullLogger<FontRegistry>());
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        var fontData = File.ReadAllBytes(fontPath);

        registry.RegisterFont("MyCustomFont", FontStyle.Regular, fontData);

        var font = registry.FindFont("MyCustomFont", FontStyle.Regular);
        Assert.NotNull(font);
        Assert.Equal("MyCustomFont", font.FamilyName);
        Assert.True(font.IsEmbedded);
    }

    [Fact]
    public void RegisterFont_WithBoldStyle_StoresBoldVariant()
    {
        var registry = new FontRegistry(new NullLogger<FontRegistry>());
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialbd.ttf");
        var fontData = File.ReadAllBytes(fontPath);

        registry.RegisterFont("MyBoldFont", FontStyle.Bold, fontData);

        var font = registry.FindFont("MyBoldFont", FontStyle.Bold);
        Assert.NotNull(font);
        Assert.True(font.IsEmbedded);
    }

    [Fact]
    public void RegisterFont_OverwritesExisting()
    {
        var registry = new FontRegistry(new NullLogger<FontRegistry>());
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        var fontData = File.ReadAllBytes(fontPath);

        registry.RegisterFont("OverwriteFont", FontStyle.Regular, fontData);
        registry.RegisterFont("OverwriteFont", FontStyle.Regular, fontData);

        var font = registry.FindFont("OverwriteFont", FontStyle.Regular);
        Assert.NotNull(font);
    }

    [Fact]
    public void GetMetrics_RegisteredFont_ReturnsMetrics()
    {
        var registry = new FontRegistry(new NullLogger<FontRegistry>());
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        var fontData = File.ReadAllBytes(fontPath);

        registry.RegisterFont("MetricsFont", FontStyle.Regular, fontData);
        var metrics = registry.GetMetrics("MetricsFont", FontStyle.Regular, 12f);

        Assert.NotNull(metrics);
        Assert.True(metrics.Ascender > 0);
        Assert.True(metrics.Descender < 0);
        Assert.True(metrics.LineHeight > 0);
        Assert.Equal("MetricsFont", metrics.FamilyName);
    }
}
