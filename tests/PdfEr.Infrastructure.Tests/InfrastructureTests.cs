using Microsoft.Extensions.Logging.Abstractions;
using PdfEr.Infrastructure.Utilities;
using PdfEr.Infrastructure.Caching;
using PdfEr.Infrastructure.PdfWriters;
using PdfEr.Infrastructure.Typography;
using PdfEr.Core.Domain.Forms;
using PdfEr.Core.Domain.Toc;
using PdfEr.Core.Domain.Typography;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.ValueObjects;
using PdfEr.Core.Domain.Enums;

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

    [Fact]
    public void AppendByteArray_AccumulatesBytes()
    {
        var buf = new PdfBuffer();
        buf.Append(new byte[] { 0, 128, 255 });
        var result = buf.ToByteArray();
        Assert.Equal(3, result.Length);
        Assert.Equal(0, result[0]);
        Assert.Equal(128, result[1]);
        Assert.Equal(255, result[2]);
    }

    [Fact]
    public void AppendByteArray_NullOrEmpty_DoesNothing()
    {
        var buf = new PdfBuffer();
        buf.Append((byte[])null!);
        buf.Append(Array.Empty<byte>());
        Assert.Equal(0, buf.Length);
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

public sealed class PdfWriterWriteDocumentLayoutTests
{
    private static PdfWriter CreateWriter()
    {
        var fontReg = new FontRegistry(new Microsoft.Extensions.Logging.Abstractions.NullLogger<FontRegistry>());
        return new PdfWriter(fontReg);
    }

    private static DocumentLayout CreateSinglePageLayout()
    {
        var size = PageSize.FromFormat(PageFormat.A4);
        var margins = new DocumentMargins(15, 15, 15, 15, 5, 5);
        var page = new PageLayout(size, PageOrientation.Portrait, margins, 1);
        return new DocumentLayout { Pages = { page } };
    }

    private static PdfConverterConfiguration DefaultConfig => new()
    {
        Title = "TestDoc",
        DefaultFontSize = 10f
    };

    private static string GetContentStream(byte[] pdf)
    {
        var text = System.Text.Encoding.ASCII.GetString(pdf);
        var streamStart = text.IndexOf("stream\n", StringComparison.Ordinal);
        if (streamStart < 0) return "";
        streamStart += 7;
        var streamEnd = text.IndexOf("\nendstream", streamStart, StringComparison.Ordinal);
        if (streamEnd < 0) streamEnd = text.Length;
        return text[streamStart..streamEnd];
    }

    [Fact]
    public void WriteDocumentLayout_BasicTextBlock_ContainsTextAndOperators()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        page.Blocks.Add(new BlockBox
        {
            TagName = "p",
            X = 15, Y = 20, Width = 180, Height = 20,
            TextContent = "Hello PDF",
            PaddingTop = 1, PaddingBottom = 1,
            PaddingLeft = 1, PaddingRight = 1
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("Hello PDF", stream);
        Assert.Contains("BT", stream);
        Assert.Contains("ET", stream);
        Assert.Contains("Tf", stream);
        Assert.Contains("Td", stream);
        Assert.Contains("Tj", stream);
        Assert.Contains("%PDF-1.7", System.Text.Encoding.ASCII.GetString(pdf, 0, 20));
    }

    [Fact]
    public void WriteDocumentLayout_BackgroundColor_ContainsFillOperator()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        var style = new CssDeclarationBlock();
        style.SetProperty("background-color", "red");
        page.Blocks.Add(new BlockBox
        {
            TagName = "div",
            X = 15, Y = 20, Width = 180, Height = 30,
            TextContent = "Bg",
            ComputedStyle = style
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("rg", stream);
        Assert.Contains("re f", stream);
    }

    [Fact]
    public void WriteDocumentLayout_BackgroundColorTransparent_SkipsFill()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        var style = new CssDeclarationBlock();
        style.SetProperty("background-color", "transparent");
        page.Blocks.Add(new BlockBox
        {
            TagName = "div",
            X = 15, Y = 20, Width = 180, Height = 30,
            TextContent = "NoBg",
            ComputedStyle = style
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("NoBg", stream);
        Assert.DoesNotContain("re f", stream);
    }

    [Fact]
    public void WriteDocumentLayout_Border_ContainsStrokeOperator()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        var style = new CssDeclarationBlock();
        style.SetProperty("border-color", "black");
        page.Blocks.Add(new BlockBox
        {
            TagName = "div",
            X = 15, Y = 20, Width = 180, Height = 30,
            TextContent = "Bordered",
            ComputedStyle = style,
            BorderTop = 1, BorderBottom = 1,
            BorderLeft = 1, BorderRight = 1
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("RG", stream);
        Assert.Contains("w", stream);
        Assert.Contains("re S", stream);
    }

    [Fact]
    public void WriteDocumentLayout_CssColor_ContainsTextColorOperator()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        var style = new CssDeclarationBlock();
        style.SetProperty("color", "blue");
        page.Blocks.Add(new BlockBox
        {
            TagName = "p",
            X = 15, Y = 20, Width = 180, Height = 20,
            TextContent = "Blue text",
            ComputedStyle = style
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        var rgLines = stream.Split('\n').Where(l => l.Trim().EndsWith("rg")).ToList();
        Assert.Single(rgLines);
        Assert.True(stream.IndexOf("rg") < stream.IndexOf("BT") || stream.IndexOf("rg") < stream.LastIndexOf("BT"),
            "rg should appear before BT");
    }

    [Fact]
    public void WriteDocumentLayout_EmptyBlockWithoutBordersOrBg_Skipped()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        page.Blocks.Add(new BlockBox
        {
            TagName = "div",
            X = 15, Y = 20, Width = 180, Height = 30
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.False(string.IsNullOrWhiteSpace(stream), "Stream should exist");
        Assert.DoesNotContain("BT", stream);
    }

    [Fact]
    public void WriteDocumentLayout_EmptyBlockWithBackground_NotSkipped()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        var style = new CssDeclarationBlock();
        style.SetProperty("background-color", "yellow");
        page.Blocks.Add(new BlockBox
        {
            TagName = "div",
            X = 15, Y = 20, Width = 180, Height = 30,
            ComputedStyle = style
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("re f", stream);
        Assert.Contains("rg", stream);
    }

    [Fact]
    public void WriteDocumentLayout_EmptyBlockWithBorder_NotSkipped()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        var style = new CssDeclarationBlock();
        style.SetProperty("border-color", "black");
        page.Blocks.Add(new BlockBox
        {
            TagName = "div",
            X = 15, Y = 20, Width = 180, Height = 30,
            ComputedStyle = style,
            BorderTop = 1
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("re S", stream);
        Assert.Contains("RG", stream);
    }

    [Fact]
    public void WriteDocumentLayout_MultipleBlocks_AllProcessed()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        page.Blocks.Add(new BlockBox
        {
            TagName = "p", TextContent = "First",
            X = 15, Y = 20, Width = 180, Height = 15
        });
        page.Blocks.Add(new BlockBox
        {
            TagName = "p", TextContent = "Second",
            X = 15, Y = 40, Width = 180, Height = 15
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("First", stream);
        Assert.Contains("Second", stream);
        Assert.Equal(2, CountOccurrences(stream, "BT"));
    }

    [Fact]
    public void WriteDocumentLayout_InlineContent_MultipleTextItems()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        var block = new BlockBox
        {
            TagName = "p",
            X = 15, Y = 20, Width = 180, Height = 15
        };
        block.InlineContent.Add(new InlineBox
        {
            Text = "Hello", Type = InlineBoxType.Text,
            X = 15, Y = 20, Width = 20, Height = 10
        });
        block.InlineContent.Add(new InlineBox
        {
            Text = "World", Type = InlineBoxType.Text,
            X = 35, Y = 20, Width = 20, Height = 10
        });
        page.Blocks.Add(block);

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("(Hello)", stream);
        Assert.Contains("(World)", stream);
    }

    [Fact]
    public void WriteDocumentLayout_ValidPdfStructure()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        page.Blocks.Add(new BlockBox
        {
            TagName = "p", TextContent = "Valid",
            X = 15, Y = 20, Width = 180, Height = 15
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.StartsWith("%PDF-1.7", text);
        Assert.Contains("/Type /Catalog", text);
        Assert.Contains("/Type /Pages", text);
        Assert.Contains("/Type /Page", text);
        Assert.Contains("/MediaBox", text);
        Assert.Contains("/Parent", text);
        Assert.Contains("/Contents", text);
        Assert.Contains("/Resources << /Font <<", text);
        Assert.Contains("xref", text);
        Assert.Contains("trailer", text);
        Assert.Contains("startxref", text);
        Assert.Contains("%%EOF", text);
    }

    [Fact]
    public void WriteDocumentLayout_TextAlignCenter_ShiftsTdPosition()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        var style = new CssDeclarationBlock();
        style.SetProperty("text-align", "center");
        page.Blocks.Add(new BlockBox
        {
            TagName = "p",
            X = 15, Y = 20, Width = 180, Height = 20,
            TextContent = "Centered",
            PaddingLeft = 1, PaddingRight = 1,
            ComputedStyle = style
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("Centered", stream);
        Assert.Contains("Td", stream);
    }

    [Fact]
    public void WriteDocumentLayout_TextAlignRight_ShiftsTdPosition()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        var style = new CssDeclarationBlock();
        style.SetProperty("text-align", "right");
        page.Blocks.Add(new BlockBox
        {
            TagName = "p",
            X = 15, Y = 20, Width = 180, Height = 20,
            TextContent = "RightAligned",
            PaddingLeft = 1, PaddingRight = 1,
            ComputedStyle = style
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("RightAligned", stream);
        Assert.Contains("Td", stream);
    }

    [Fact]
    public void WriteDocumentLayout_TextAlignJustify_UsesTwOperator()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        var style = new CssDeclarationBlock();
        style.SetProperty("text-align", "justify");
        page.Blocks.Add(new BlockBox
        {
            TagName = "p",
            X = 15, Y = 20, Width = 180, Height = 20,
            TextContent = "This is justified text with several words",
            PaddingLeft = 1, PaddingRight = 1,
            ComputedStyle = style
        });

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("Tw", stream);
        Assert.Contains("Td", stream);
    }

    [Fact]
    public void WriteDocumentLayout_TextDecorationUnderline_ContainsLineOperators()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        var style = new CssDeclarationBlock();
        style.SetProperty("text-decoration-line", "underline");
        var block = new BlockBox
        {
            TagName = "p",
            X = 15, Y = 20, Width = 180, Height = 20,
            ComputedStyle = style
        };
        block.InlineContent.Add(new InlineBox
        {
            Text = "Underlined",
            Type = InlineBoxType.Text,
            X = 15, Y = 20, Width = 40, Height = 10,
            ComputedStyle = style
        });
        page.Blocks.Add(block);

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("Underlined", stream);
        Assert.Contains("m", stream);
        Assert.Contains("l S", stream);
    }

    [Fact]
    public void WriteDocumentLayout_TextDecorationLineThrough_ContainsLineOperators()
    {
        var writer = CreateWriter();
        var layout = CreateSinglePageLayout();
        var page = layout.Pages[0];
        var style = new CssDeclarationBlock();
        style.SetProperty("text-decoration-line", "line-through");
        var block = new BlockBox
        {
            TagName = "p",
            X = 15, Y = 20, Width = 180, Height = 20,
            ComputedStyle = style
        };
        block.InlineContent.Add(new InlineBox
        {
            Text = "Strikethrough",
            Type = InlineBoxType.Text,
            X = 15, Y = 20, Width = 50, Height = 10,
            ComputedStyle = style
        });
        page.Blocks.Add(block);

        var pdf = writer.WriteDocumentLayout(layout, DefaultConfig);
        var stream = GetContentStream(pdf);

        Assert.Contains("Strikethrough", stream);
        Assert.Contains("m", stream);
        Assert.Contains("l S", stream);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
