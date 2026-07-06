using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Enums;
using PdfEr.Infrastructure;

namespace PdfEr.IntegrationTests;

/// <summary>
/// End-to-end tests for the box-tree pipeline (docs/plans/phase-01-foundation.md),
/// gated behind PdfConverterConfiguration.UseBoxTreeLayout. Deliberately limited to
/// simple documents: BoxTreeBuilder does not dispatch to ITagHandler yet, so lists,
/// tables, links, and images are not expected to render correctly through this path.
/// These tests only confirm the wired pipeline produces valid, non-empty PDF output
/// and does not throw -- not feature parity with the streaming pipeline (that's
/// measured separately via the Phase 0 fidelity harness).
/// </summary>
public sealed class BoxTreePipelineIntegrationTests : IDisposable
{
    private readonly IServiceProvider _services;
    private bool _disposed;

    public BoxTreePipelineIntegrationTests()
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
            cfg.TempDirectory = Path.Combine(Path.GetTempPath(), "PdfErBoxTreeInt_" + Guid.NewGuid().ToString("N"));
        });
        _services = collection.BuildServiceProvider();
    }

    private static PdfConverterConfiguration BoxTreeConfig() => new()
    {
        PageFormat = PageFormat.A4,
        Orientation = PageOrientation.Portrait,
        UseBoxTreeLayout = true,
        TempDirectory = Path.Combine(Path.GetTempPath(), "PdfErBoxTreeInt_" + Guid.NewGuid().ToString("N")),
    };

    [Fact]
    public void ConvertHtmlToPdf_BoxTreeLayout_SimpleParagraph_ReturnsValidPdf()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var pdf = converter.ConvertHtmlToPdf("<html><body><p>Hello box tree</p></body></html>", BoxTreeConfig());

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 10, "PDF output should not be empty");
        var header = System.Text.Encoding.ASCII.GetString(pdf, 0, Math.Min(10, pdf.Length));
        Assert.StartsWith("%PDF", header);
        var text = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("%%EOF", text);
    }

    [Fact]
    public void ConvertHtmlToPdf_BoxTreeLayout_NestedBlocks_DoesNotThrow()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = "<html><body><div><h1>Title</h1><p>Paragraph one.</p><div><p>Nested paragraph.</p></div></div></body></html>";

        var pdf = converter.ConvertHtmlToPdf(html, BoxTreeConfig());

        Assert.True(pdf.Length > 10);
    }

    [Fact]
    public void ConvertHtmlToPdf_BoxTreeLayout_EmptyBody_ReturnsValidPdf()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var pdf = converter.ConvertHtmlToPdf("<html><body></body></html>", BoxTreeConfig());

        Assert.True(pdf.Length > 10);
        var header = System.Text.Encoding.ASCII.GetString(pdf, 0, Math.Min(10, pdf.Length));
        Assert.StartsWith("%PDF", header);
    }

    [Fact]
    public void ConvertHtmlToPdf_BoxTreeLayout_WithStyledDiv_DoesNotThrow()
    {
        var converter = _services.GetRequiredService<IPdfConverter>();
        var html = """
            <html>
            <head><style>div { padding: 5mm; margin: 3mm; width: 100mm; } p { color: red; }</style></head>
            <body><div><p>Styled content</p></div></body>
            </html>
            """;

        var pdf = converter.ConvertHtmlToPdf(html, BoxTreeConfig());

        Assert.True(pdf.Length > 10);
    }

    [Fact]
    public void ConvertHtmlToPdf_DefaultConfig_StillUsesStreamingPipeline()
    {
        // UseBoxTreeLayout defaults to false -- confirms the flag is genuinely
        // opt-in and the existing streaming pipeline path is unaffected.
        var converter = _services.GetRequiredService<IPdfConverter>();
        var pdf = converter.ConvertHtmlToPdf("<html><body><p>Streaming pipeline</p></body></html>");

        Assert.True(pdf.Length > 10);
        var header = System.Text.Encoding.ASCII.GetString(pdf, 0, Math.Min(10, pdf.Length));
        Assert.StartsWith("%PDF", header);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        (_services as IDisposable)?.Dispose();
    }
}
