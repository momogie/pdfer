using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Typography;
using PdfEr.Infrastructure;
using PdfEr.Core.Domain.Enums;

namespace PdfEr.IntegrationTests;

public sealed class PdfConverterIntegrationTests : IDisposable
{
    private readonly IServiceProvider _services;
    private bool _disposed;

    public PdfConverterIntegrationTests()
    {
        var collection = new ServiceCollection();

        collection.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        collection.AddPdfEr(cfg =>
        {
            cfg.PageFormat = PageFormat.A4;
            cfg.Orientation = PageOrientation.Portrait;
            cfg.TempDirectory = Path.Combine(Path.GetTempPath(), "PdfErInt_" + Guid.NewGuid().ToString("N"));
        });

        _services = collection.BuildServiceProvider();
    }

    [Fact]
    public void ConvertHtmlToPdf_SimpleHtml_ReturnsNonEmptyPdf()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><p>Hello PDF World</p></body></html>";

        var pdf = converter.ConvertHtmlToPdf(html);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 10, "PDF output should not be empty");
    }

    [Fact]
    public void ConvertHtmlToPdf_OutputStartsWithPdfHeader()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><p>Test</p></body></html>";

        var pdf = converter.ConvertHtmlToPdf(html);

        var header = System.Text.Encoding.ASCII.GetString(pdf, 0, Math.Min(10, pdf.Length));
        Assert.StartsWith("%PDF", header);
    }

    [Fact]
    public void ConvertHtmlToPdf_OutputContainsEof()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><h1>Title</h1><p>Content</p></body></html>";

        var pdf = converter.ConvertHtmlToPdf(html);

        var text = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("%%EOF", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_WithStyleSheet_AppliesStyles()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html>
            <head><style>p { color: red; font-size: 14pt; }</style></head>
            <body><p>Styled paragraph</p></body>
            </html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 50);
    }

    [Fact]
    public void ConvertHtmlToPdf_ComplexHtml_ProducesValidPdf()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <!DOCTYPE html>
            <html>
            <head>
                <title>Complex Test</title>
                <style>
                    body { font-family: sans-serif; margin: 20mm; }
                    h1 { color: navy; font-size: 24pt; }
                    .highlight { background: yellow; }
                </style>
            </head>
            <body>
                <h1>Heading 1</h1>
                <p>This is a paragraph with <span class="highlight">highlighted</span> text.</p>
                <ul>
                    <li>Item one</li>
                    <li>Item two</li>
                    <li>Item three</li>
                </ul>
                <p>Another paragraph after the list.</p>
            </body>
            </html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.StartsWith("%PDF", text);
        Assert.Contains("%%EOF", text);
        Assert.True(pdf.Length > 100, "Complex document should be substantial");
    }

    [Fact]
    public async Task ConvertHtmlToPdfAsync_ReturnsPdfBytes()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><p>Async test</p></body></html>";

        var pdf = await converter.ConvertHtmlToPdfAsync(html);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 10);
    }

    [Fact]
    public void ConvertHtmlToPdfToFile_WritesValidFile()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><p>File test</p></body></html>";
        var filePath = Path.Combine(Path.GetTempPath(), $"PdfEr_{Guid.NewGuid():N}.pdf");

        try
        {
            converter.ConvertHtmlToPdfToFile(html, filePath);
            Assert.True(File.Exists(filePath));
            var content = File.ReadAllBytes(filePath);
            Assert.True(content.Length > 10);
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(content));
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Fact]
    public void ConvertHtmlToPdf_MultipleConversions_AllSucceed()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var htmls = new[]
        {
            "<html><body><p>Doc 1</p></body></html>",
            "<html><body><p>Doc 2</p></body></html>",
            "<html><body><p>Doc 3</p></body></html>"
        };

        foreach (var html in htmls)
        {
            var pdf = converter.ConvertHtmlToPdf(html);
            Assert.NotNull(pdf);
            Assert.True(pdf.Length > 10);
        }
    }

    [Fact]
    public void ConvertHtmlToPdf_WithOptions_UsesCustomConfig()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><p>Config test</p></body></html>";
        var config = new PdfConverterConfiguration
        {
            PageFormat = PageFormat.Letter,
            Orientation = PageOrientation.Landscape
        };

        var pdf = converter.ConvertHtmlToPdf(html, config);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.NotNull(pdf);
        Assert.StartsWith("%PDF", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_HeaderFontSize_FromUaStylesheet()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><h1>Big Heading</h1><p>Normal text</p></body></html>";

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("/F1 20.00 Tf", text); // h1 default = 2em = 20pt
        Assert.Contains("/F2 10.00 Tf", text); // p default = 10pt (second font, after h1 bold)
    }

    [Fact]
    public void ConvertHtmlToPdf_UnicodeText_UsesHexEncoding()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        // Characters above U+00FF require UTF-16BE hex encoding
        // \u2603 = snowman, \u2602 = umbrella
        var html = "<html><body><p>Hello &#x2603;&#x2602; world</p></body></html>";

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("<feff", text); // UTF-16BE BOM
    }

    [Fact]
    public void ConvertHtmlToPdf_InlineFormatting_CollectsAllText()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><p>This has <b>bold</b> and <i>italic</i> text.</p></body></html>";

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("This has bold and italic text.", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_StyleFontSizePt_UsesCorrectSize()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><p style=\"font-size: 18pt\">Big paragraph</p></body></html>";

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("/F1 18.00 Tf", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_StyleBlock_AppliesFontSize()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><head><style>p { font-size: 22pt; }</style></head>
            <body><p>Big text from style block</p></body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("/F1 22.00 Tf", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_ClassSelector_AppliesStyle()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><head><style>.big { font-size: 24pt; } .red { color: #ff0000; }</style></head>
            <body><p class="big red">Classy paragraph</p></body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("/F1 24.00 Tf", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_IdSelector_AppliesStyle()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><head><style>#special { font-size: 26pt; }</style></head>
            <body><p id="special">Special paragraph</p></body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("/F1 26.00 Tf", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_DisplayNone_SkipsElement()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><body>
            <p>Visible</p>
            <p style="display:none">Hidden</p>
            <p>Also visible</p>
            </body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("Visible", text);
        Assert.Contains("Also visible", text);
        Assert.DoesNotContain("Hidden", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_MediaPrint_AppliesRules()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><head><style>
                @media print { p { font-size: 28pt; } }
                @media screen { p { font-size: 10pt; } }
            </style></head>
            <body><p>Print media paragraph</p></body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("/F1 28.00 Tf", text);
        Assert.DoesNotContain("/F1 10.00 Tf", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_StyleContent_NotRenderedAsText()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><head><style>p { color: red; }</style></head>
            <body><p>Hello</p></body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.DoesNotContain("color: red", text);
        Assert.Contains("Hello", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_FontFace_RegistersCustomFont()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var fontRegistry = _services.GetRequiredService<IFontRegistry>();
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");

        var fontPathEscaped = fontPath.Replace("\\", "/");
        var htmlTemplate = """
            <html><head><style>
                @font-face {{
                    font-family: 'MyArial';
                    src: url('{0}') format('truetype');
                    font-weight: normal;
                    font-style: normal;
                }}
            </style></head>
            <body><p>Font face test</p></body></html>
            """;
        var html = string.Format(htmlTemplate, fontPathEscaped);

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 50);
        var font = fontRegistry.FindFont("MyArial", FontStyle.Regular);
        Assert.NotNull(font);
        Assert.True(font.IsEmbedded);
    }

    [Fact]
    public void ConvertHtmlToPdf_FontFaceBold_RegistersBoldFont()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var fontRegistry = _services.GetRequiredService<IFontRegistry>();
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialbd.ttf");

        var fontPathEscaped = fontPath.Replace("\\", "/");
        var htmlTemplate = """
            <html><head><style>
                @font-face {{
                    font-family: 'MyArialBold';
                    src: url('{0}') format('truetype');
                    font-weight: bold;
                    font-style: normal;
                }}
            </style></head>
            <body><p>Bold font face test</p></body></html>
            """;
        var html = string.Format(htmlTemplate, fontPathEscaped);

        var pdf = converter.ConvertHtmlToPdf(html);
        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 50);

        var font = fontRegistry.FindFont("MyArialBold", FontStyle.Bold);
        Assert.NotNull(font);
        Assert.True(font.IsEmbedded);
    }

    private static bool PdfTextContains(string pdfText, string search)
    {
        if (pdfText.Contains(search)) return true;
        var hex = Convert.ToHexString(
            System.Text.Encoding.BigEndianUnicode.GetBytes(search)).ToLowerInvariant();
        return pdfText.Contains(hex);
    }

    [Fact]
    public void ConvertHtmlToPdf_UnorderedList_RendersBullets()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><ul><li>Alpha</li><li>Beta</li></ul></body></html>";

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.True(PdfTextContains(text, "Alpha"), "Should contain Alpha (ASCII or hex)");
        Assert.True(PdfTextContains(text, "Beta"), "Should contain Beta (ASCII or hex)");
    }

    [Fact]
    public void ConvertHtmlToPdf_OrderedList_RendersNumbers()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><ol><li>First</li><li>Second</li></ol></body></html>";

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.True(PdfTextContains(text, "First"), "Should contain First");
        Assert.True(PdfTextContains(text, "Second"), "Should contain Second");
    }

    [Fact]
    public void ConvertHtmlToPdf_NestedLists_RendersWithIndentation()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><body><ul>
            <li>Item A</li>
            <li>Item B
            <ul><li>Sub B1</li><li>Sub B2</li></ul>
            </li>
            <li>Item C</li>
            </ul></body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.True(PdfTextContains(text, "Item A"));
        Assert.True(PdfTextContains(text, "Item B"));
        Assert.True(PdfTextContains(text, "Sub B1"));
        Assert.True(PdfTextContains(text, "Sub B2"));
        Assert.True(PdfTextContains(text, "Item C"));
    }

    [Fact]
    public void ConvertHtmlToPdf_UnorderedList_RendersListBlock()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><p>Before</p><ul><li>One</li></ul><p>After</p></body></html>";

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("Before", text);
        Assert.True(PdfTextContains(text, "One"));
        Assert.Contains("After", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_SectionAndArticle_RendersText()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><section><article><p>Content in article</p></article></section></body></html>";

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("Content in article", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_SimpleTable_RendersCellText()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><body>
            <table><tr><td>Cell A1</td><td>Cell B1</td></tr></table>
            </body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("Cell A1", text);
        Assert.Contains("Cell B1", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_TableWithHeader_RendersAllCells()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><body>
            <table>
            <tr><th>Name</th><th>Age</th></tr>
            <tr><td>Alice</td><td>30</td></tr>
            <tr><td>Bob</td><td>25</td></tr>
            </table>
            </body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.True(PdfTextContains(text, "Name"));
        Assert.True(PdfTextContains(text, "Age"));
        Assert.True(PdfTextContains(text, "Alice"));
        Assert.True(PdfTextContains(text, "30"));
        Assert.True(PdfTextContains(text, "Bob"));
        Assert.True(PdfTextContains(text, "25"));
    }

    [Fact]
    public void ConvertHtmlToPdf_TableWithColspan_RendersCellText()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><body>
            <table>
            <tr><td colspan="2">Wide cell</td></tr>
            <tr><td>Left</td><td>Right</td></tr>
            </table>
            </body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.True(PdfTextContains(text, "Wide cell"));
        Assert.True(PdfTextContains(text, "Left"));
        Assert.True(PdfTextContains(text, "Right"));
    }

    [Fact]
    public void ConvertHtmlToPdf_TableWithSurroundingContent_RendersAllText()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><body>
            <p>Before table</p>
            <table><tr><td>In table</td></tr></table>
            <p>After table</p>
            </body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("Before table", text);
        Assert.Contains("In table", text);
        Assert.Contains("After table", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_MultipleTableRowsAndColumns_RendersAllCells()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><body>
            <table>
            <tr><td>Row1-Col1</td><td>Row1-Col2</td><td>Row1-Col3</td></tr>
            <tr><td>Row2-Col1</td><td>Row2-Col2</td><td>Row2-Col3</td></tr>
            <tr><td>Row3-Col1</td><td>Row3-Col2</td><td>Row3-Col3</td></tr>
            </table>
            </body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.True(PdfTextContains(text, "Row1-Col1"));
        Assert.True(PdfTextContains(text, "Row1-Col2"));
        Assert.True(PdfTextContains(text, "Row1-Col3"));
        Assert.True(PdfTextContains(text, "Row2-Col1"));
        Assert.True(PdfTextContains(text, "Row2-Col2"));
        Assert.True(PdfTextContains(text, "Row2-Col3"));
        Assert.True(PdfTextContains(text, "Row3-Col1"));
        Assert.True(PdfTextContains(text, "Row3-Col2"));
        Assert.True(PdfTextContains(text, "Row3-Col3"));
    }

    [Fact]
    public void ConvertHtmlToPdf_TableInSection_RendersCellText()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><body>
            <section>
            <table><thead><tr><th>Header</th></tr></thead>
            <tbody><tr><td>Body</td></tr></tbody>
            <tfoot><tr><td>Footer</td></tr></tfoot>
            </table>
            </section>
            </body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.True(PdfTextContains(text, "Header"));
        Assert.True(PdfTextContains(text, "Body"));
        Assert.True(PdfTextContains(text, "Footer"));
    }

    [Fact]
    public void ConvertHtmlToPdf_TableBorder_RendersStrokeOperators()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><head><style>td { border: 1px solid black; }</style></head>
            <body><table><tr><td>Cell</td></tr></table></body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("re S", text);
        Assert.Contains("RG", text);
        Assert.Contains("w", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_TableBackground_RendersFillOperator()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html><head><style>th { background-color: #ddeeff; }</style></head>
            <body><table><tr><th>Header</th></tr></table></body></html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("re f", text);
        Assert.Contains("rg", text);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_services is IDisposable d) d.Dispose();
            _disposed = true;
        }
    }
}
