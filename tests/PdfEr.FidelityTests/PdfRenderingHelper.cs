using Microsoft.Playwright;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Enums;
using PdfEr.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PdfEr.FidelityTests;

public class PdfRenderingHelper : IAsyncDisposable
{
    private readonly IBrowser? _browser;
    private readonly IPdfConverter _pdfConverter;
    private readonly string _tempDir;
    private const int DPI = 150;

    private PdfRenderingHelper(IBrowser? browser, IPdfConverter converter, string tempDir)
    {
        _browser = browser;
        _pdfConverter = converter;
        _tempDir = tempDir;
        Directory.CreateDirectory(tempDir);
    }

    public static async Task<PdfRenderingHelper> CreateAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"PdfEr_Fidelity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        IBrowser? browser = null;
        try
        {
            var playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync();
        }
        catch
        {
            Console.WriteLine("Warning: Could not launch Chromium; Chromium rendering will be skipped.");
        }

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Warning);
        });
        services.AddPdfEr(cfg =>
        {
            cfg.PageFormat = PageFormat.A4;
            cfg.Orientation = PageOrientation.Portrait;
            cfg.TempDirectory = Path.Combine(tempDir, "pdfer");
        });
        var sp = services.BuildServiceProvider();
        var converter = sp.GetRequiredService<IPdfConverter>();

        return new PdfRenderingHelper(browser, converter, tempDir);
    }

    public async Task<byte[]> RenderHtmlViaChromiumAsync(string htmlContent)
    {
        if (_browser == null)
            throw new InvalidOperationException("Chromium not available");

        var htmlFile = Path.Combine(_tempDir, $"temp_{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(htmlFile, htmlContent);

        var pdfFile = Path.Combine(_tempDir, $"chromium_{Guid.NewGuid():N}.pdf");
        var context = await _browser.NewContextAsync();
        var page = await context.NewPageAsync();

        try
        {
            await page.GotoAsync($"file://{htmlFile}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var pdfBytes = await page.PdfAsync(new() { Path = pdfFile, Format = "A4" });
            return pdfBytes;
        }
        finally
        {
            await page.CloseAsync();
            await context.CloseAsync();
            File.Delete(htmlFile);
        }
    }

    public byte[] RenderHtmlViaPdfEr(string htmlContent) =>
        _pdfConverter.ConvertHtmlToPdf(htmlContent);

    /// <summary>
    /// Renders via the box-tree pipeline (docs/plans/phase-01-foundation.md) instead
    /// of the default streaming pipeline, for comparing the two independently.
    /// </summary>
    public byte[] RenderHtmlViaPdfErBoxTree(string htmlContent) =>
        _pdfConverter.ConvertHtmlToPdf(htmlContent, new PdfConverterConfiguration
        {
            PageFormat = PageFormat.A4,
            Orientation = PageOrientation.Portrait,
            UseBoxTreeLayout = true,
        });

    public Task<Image<Rgba32>> RasterizePdfAsync(byte[] pdfBytes)
    {
        // PDFium-backed rasterizer (via PDFtoImage) — renders actual PDF content,
        // unlike shelling out to Chromium's PDF viewer which is unreliable headless.
        using var skBitmap = PDFtoImage.Conversion.ToImage(pdfBytes, (System.Index)0,
            options: new PDFtoImage.RenderOptions(Dpi: DPI));

        using var pngStream = new MemoryStream();
        skBitmap.Encode(pngStream, SkiaSharp.SKEncodedImageFormat.Png, 100);
        pngStream.Position = 0;

        var image = Image.Load<Rgba32>(pngStream);
        return Task.FromResult(image);
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
            await _browser.CloseAsync();

        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }
}
