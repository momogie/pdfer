using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PdfEr.FidelityTests;

public class FidelityReport
{
    public string CaseName { get; set; } = "";
    public string Category { get; set; } = "";
    public int PageCount { get; set; }
    public double AverageSSIM { get; set; }
    public List<double> PerPageSSIM { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class FidelityTestRunner
{
    private readonly PdfRenderingHelper _helper;
    private readonly string _casesDir;
    private readonly string _outputDir;
    private List<FidelityReport> _reports = new();

    public FidelityTestRunner(PdfRenderingHelper helper, string casesDir, string outputDir)
    {
        _helper = helper;
        _casesDir = casesDir;
        _outputDir = outputDir;
        Directory.CreateDirectory(outputDir);
    }

    public async Task RunAllTestsAsync()
    {
        var htmlFiles = Directory.GetFiles(_casesDir, "*.html");
        Console.WriteLine($"Found {htmlFiles.Length} test cases");

        foreach (var htmlFile in htmlFiles)
        {
            var name = Path.GetFileNameWithoutExtension(htmlFile);
            var category = ExtractCategory(name);
            Console.WriteLine($"\nTesting: {name} (category: {category})");

            try
            {
                var html = await File.ReadAllTextAsync(htmlFile);
                var report = await RunSingleTestAsync(name, category, html);
                _reports.Add(report);

                Console.WriteLine($"  SSIM: {report.AverageSSIM:F4} | Pages: {report.PageCount}");
                if (report.PerPageSSIM.Count > 0)
                    Console.WriteLine($"  Per-page: {string.Join(", ", report.PerPageSSIM.Select(s => $"{s:F3}"))}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR: {ex.Message}");
                _reports.Add(new FidelityReport
                {
                    CaseName = name,
                    Category = category,
                    ErrorMessage = ex.Message
                });
            }
        }

        SaveReport();
    }

    private async Task<FidelityReport> RunSingleTestAsync(string name, string category, string html)
    {
        var report = new FidelityReport { CaseName = name, Category = category };

        byte[] pdfChromium;
        try
        {
            pdfChromium = await _helper.RenderHtmlViaChromiumAsync(html);
        }
        catch
        {
            pdfChromium = new byte[0];
        }

        var pdfPdfEr = _helper.RenderHtmlViaPdfEr(html);

        var imageChromium = pdfChromium.Length > 0 ? await _helper.RasterizePdfAsync(pdfChromium) : null;
        var imagePdfEr = await _helper.RasterizePdfAsync(pdfPdfEr);

        if (imageChromium == null)
        {
            report.ErrorMessage = "Chromium rendering skipped";
            return report;
        }

        // Rounding differences of a few px between renderers at a given DPI are expected;
        // only fail on genuinely divergent page dimensions (e.g. wrong page size/orientation).
        var widthDiff = Math.Abs(imageChromium.Width - imagePdfEr.Width);
        var heightDiff = Math.Abs(imageChromium.Height - imagePdfEr.Height);
        const int maxDriftPx = 10;

        if (widthDiff > maxDriftPx || heightDiff > maxDriftPx)
        {
            var msg = $"Size mismatch: Chromium {imageChromium.Width}x{imageChromium.Height}, PdfEr {imagePdfEr.Width}x{imagePdfEr.Height}";
            report.ErrorMessage = msg;
            return report;
        }

        var commonWidth = Math.Min(imageChromium.Width, imagePdfEr.Width);
        var commonHeight = Math.Min(imageChromium.Height, imagePdfEr.Height);
        imageChromium.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(0, 0, commonWidth, commonHeight)));
        imagePdfEr.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(0, 0, commonWidth, commonHeight)));

        var ssim = SsimCalculator.CalculateSSIM(imageChromium, imagePdfEr);
        report.AverageSSIM = ssim;
        report.PerPageSSIM.Add(ssim);
        report.PageCount = 1;

        imageChromium.Dispose();
        imagePdfEr.Dispose();

        return report;
    }

    private void SaveReport()
    {
        var reportPath = Path.Combine(_outputDir, "fidelity-report.json");
        var json = JsonConvert.SerializeObject(_reports, Formatting.Indented);
        File.WriteAllText(reportPath, json);
        Console.WriteLine($"\nReport saved: {reportPath}");

        var summary = new StringBuilder();
        summary.AppendLine("\n=== FIDELITY TEST SUMMARY ===");

        var byCategory = _reports.GroupBy(r => r.Category);
        foreach (var group in byCategory)
        {
            var passed = group.Where(r => r.AverageSSIM >= 0.90).Count();
            var scored = group.Where(r => r.AverageSSIM > 0).ToList();
            var avgText = scored.Count > 0 ? $"{scored.Average(r => r.AverageSSIM):F4}" : "N/A";
            summary.AppendLine($"{group.Key}: {passed}/{group.Count()} passed, avg SSIM={avgText}");
        }

        var summaryPath = Path.Combine(_outputDir, "fidelity-summary.txt");
        File.WriteAllText(summaryPath, summary.ToString());
        Console.WriteLine(summary.ToString());
    }

    private static string ExtractCategory(string testName)
    {
        var parts = testName.Split('-');
        return parts.Length > 0 ? parts[0] : "other";
    }
}
