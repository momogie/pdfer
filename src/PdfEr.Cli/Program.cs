using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Enums;
using PdfEr.Infrastructure;

var cmdArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

if (cmdArgs.Length == 0 || cmdArgs.Contains("--help") || cmdArgs.Contains("-h"))
{
    Console.WriteLine("PdfEr HTML-to-PDF Converter");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  PdfEr.Cli <input.html> <output.pdf> [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --format <A4|Letter|Legal|...>     Page format (default: A4)");
    Console.WriteLine("  --orientation <portrait|landscape>  Page orientation (default: portrait)");
    Console.WriteLine("  --margin <mm>                       All margins in mm (default: 15)");
    Console.WriteLine("  --margin-top <mm>                   Top margin in mm");
    Console.WriteLine("  --margin-bottom <mm>                Bottom margin in mm");
    Console.WriteLine("  --margin-left <mm>                  Left margin in mm");
    Console.WriteLine("  --margin-right <mm>                 Right margin in mm");
    Console.WriteLine("  --dpi <n>                           Output DPI (default: 96)");
    Console.WriteLine("  --compress <true|false>             Enable compression (default: true)");
    Console.WriteLine("  --encrypt                           Enable encryption");
    Console.WriteLine("  --user-password <pwd>               User password");
    Console.WriteLine("  --owner-password <pwd>              Owner password");
    Console.WriteLine("  --debug                             Enable debug logging");
    Console.WriteLine("  --version                           Show version");
    return 0;
}

if (cmdArgs.Contains("--version"))
{
    Console.WriteLine("PdfEr v1.0.0");
    Console.WriteLine("HTML-to-PDF Converter (C# .NET 10)");
    Console.WriteLine("MIT License");
    return 0;
}

string inputPath = cmdArgs[0];
string outputPath = cmdArgs[1];

var opts = ParseOptions(cmdArgs.Skip(2).ToArray());

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(opts.Debug ? LogLevel.Debug : LogLevel.Information));

services.AddPdfEr(cfg =>
{
    cfg.PageFormat = opts.PageFormat;
    cfg.Orientation = opts.Orientation;
    cfg.MarginTop = opts.MarginTop;
    cfg.MarginBottom = opts.MarginBottom;
    cfg.MarginLeft = opts.MarginLeft;
    cfg.MarginRight = opts.MarginRight;
    cfg.Dpi = opts.Dpi;
    cfg.EnableCompression = opts.Compress;
    cfg.EnableEncryption = opts.Encrypt;
    cfg.UserPassword = opts.UserPassword;
    cfg.OwnerPassword = opts.OwnerPassword;
    cfg.DebugMode = opts.Debug;
});

var sp = services.BuildServiceProvider();
var logger = sp.GetRequiredService<ILogger<Program>>();

if (!File.Exists(inputPath))
{
    logger.LogError("Input file not found: {Path}", inputPath);
    return 1;
}

logger.LogInformation("Converting {Input} -> {Output}", Path.GetFileName(inputPath), Path.GetFileName(outputPath));
logger.LogInformation("Format: {Format}, Orientation: {Orientation}",
    opts.PageFormat.ToString().ToUpperInvariant(), opts.Orientation);

var html = await File.ReadAllTextAsync(inputPath);
var converter = sp.GetRequiredService<IPdfConverter>();
await converter.ConvertHtmlToPdfToFileAsync(html, outputPath);

logger.LogInformation("PDF written: {Path} ({Size} bytes)", outputPath, new FileInfo(outputPath).Length);
return 0;

static CliOptions ParseOptions(string[] optsArgs)
{
    var opts = new CliOptions();
    for (int i = 0; i < optsArgs.Length; i++)
    {
        switch (optsArgs[i].ToLowerInvariant())
        {
            case "--format" when i + 1 < optsArgs.Length:
                opts.PageFormat = optsArgs[++i].ToLowerInvariant() switch
                {
                    "a0" => PageFormat.A0, "a1" => PageFormat.A1, "a2" => PageFormat.A2,
                    "a3" => PageFormat.A3, "a4" => PageFormat.A4, "a5" => PageFormat.A5,
                    "a6" => PageFormat.A6, "b0" => PageFormat.B0, "b1" => PageFormat.B1,
                    "b2" => PageFormat.B2, "b3" => PageFormat.B3, "b4" => PageFormat.B4,
                    "b5" => PageFormat.B5, "b6" => PageFormat.B6,
                    "letter" => PageFormat.Letter, "legal" => PageFormat.Legal,
                    "ledger" => PageFormat.Ledger, "tabloid" => PageFormat.Tabloid,
                    "executive" => PageFormat.Executive,
                    _ => PageFormat.A4
                };
                break;
            case "--orientation" when i + 1 < optsArgs.Length:
                opts.Orientation = optsArgs[++i].ToLowerInvariant() switch
                {
                    "landscape" => PageOrientation.Landscape,
                    _ => PageOrientation.Portrait
                };
                break;
            case "--margin" when i + 1 < optsArgs.Length:
                if (float.TryParse(optsArgs[++i], out var m))
                    opts.MarginTop = opts.MarginBottom = opts.MarginLeft = opts.MarginRight = m;
                break;
            case "--margin-top" when i + 1 < optsArgs.Length:
                if (float.TryParse(optsArgs[++i], out var mt)) opts.MarginTop = mt;
                break;
            case "--margin-bottom" when i + 1 < optsArgs.Length:
                if (float.TryParse(optsArgs[++i], out var mb)) opts.MarginBottom = mb;
                break;
            case "--margin-left" when i + 1 < optsArgs.Length:
                if (float.TryParse(optsArgs[++i], out var ml)) opts.MarginLeft = ml;
                break;
            case "--margin-right" when i + 1 < optsArgs.Length:
                if (float.TryParse(optsArgs[++i], out var mr)) opts.MarginRight = mr;
                break;
            case "--dpi" when i + 1 < optsArgs.Length:
                if (int.TryParse(optsArgs[++i], out var d)) opts.Dpi = d;
                break;
            case "--compress" when i + 1 < optsArgs.Length:
                opts.Compress = optsArgs[++i].ToLowerInvariant() == "true";
                break;
            case "--encrypt":
                opts.Encrypt = true;
                break;
            case "--user-password" when i + 1 < optsArgs.Length:
                opts.UserPassword = optsArgs[++i];
                break;
            case "--owner-password" when i + 1 < optsArgs.Length:
                opts.OwnerPassword = optsArgs[++i];
                break;
            case "--debug":
                opts.Debug = true;
                break;
        }
    }
    return opts;
}

public sealed class CliOptions
{
    public PageFormat PageFormat { get; set; } = PageFormat.A4;
    public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;
    public float MarginTop { get; set; } = 15f;
    public float MarginBottom { get; set; } = 15f;
    public float MarginLeft { get; set; } = 15f;
    public float MarginRight { get; set; } = 15f;
    public int Dpi { get; set; } = 96;
    public bool Compress { get; set; } = true;
    public bool Encrypt { get; set; }
    public string? UserPassword { get; set; }
    public string? OwnerPassword { get; set; }
    public bool Debug { get; set; }
}

public partial class Program { }
