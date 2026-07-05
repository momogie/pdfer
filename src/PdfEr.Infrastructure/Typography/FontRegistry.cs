using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Typography;

namespace PdfEr.Infrastructure.Typography;

public sealed class FontRegistry : IFontRegistry, IDisposable
{
    private readonly ConcurrentDictionary<string, FontMetrics> _metricsCache = new();
    private readonly Dictionary<string, FontDefinition> _loadedFonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _searchedDirectories = new();
    private readonly string[] _searchDirectories;
    private readonly ILogger<FontRegistry> _logger;
    private readonly Dictionary<string, string[]> _fallbackMap;
    private bool _disposed;

    private static readonly (string family, FontStyle style, string pattern)[] BuiltInFonts =
    [
        ("Helvetica", FontStyle.Regular, "helvetica*"),
        ("Helvetica", FontStyle.Bold, "helvetica-bold*"),
        ("Helvetica", FontStyle.Italic, "helvetica-oblique*"),
        ("Helvetica", FontStyle.BoldItalic, "helvetica-boldoblique*"),
        ("Times", FontStyle.Regular, "times*"),
        ("Times", FontStyle.Bold, "times-bold*"),
        ("Times", FontStyle.Italic, "times-italic*"),
        ("Times", FontStyle.BoldItalic, "times-bolditalic*"),
        ("Courier", FontStyle.Regular, "cour*"),
        ("Courier", FontStyle.Bold, "cour-bold*"),
        ("Courier", FontStyle.Italic, "cour-oblique*"),
        ("Courier", FontStyle.BoldItalic, "cour-boldoblique*"),
    ];

    public FontRegistry(ILogger<FontRegistry> logger, string[]? searchDirectories = null)
    {
        _logger = logger;
        _searchDirectories = searchDirectories ?? ["/usr/share/fonts", "/usr/local/share/fonts",
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts")];

        _fallbackMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["sans-serif"] = ["Helvetica", "Arial", "DejaVu Sans", "Liberation Sans"],
            ["serif"] = ["Times New Roman", "Times", "DejaVu Serif", "Liberation Serif"],
            ["monospace"] = ["Courier New", "Courier", "DejaVu Sans Mono", "Liberation Mono"],
            ["cursive"] = ["Comic Sans MS"],
            ["fantasy"] = ["Impact"],
        };

        RegisterBuiltInFallbacks();
    }

    public IReadOnlyList<string> AvailableFamilies => _loadedFonts.Keys.Distinct().ToList();

    public string? ResolveFilePath(string familyName, FontStyle style)
    {
        var font = FindFont(familyName, style);
        return font?.FilePath ?? FindFont(familyName, FontStyle.Regular)?.FilePath;
    }

    public FontDefinition? FindFont(string familyName, FontStyle style)
    {
        var key = $"{familyName}:{style}";
        if (_loadedFonts.TryGetValue(key, out var cached))
            return cached;

        var def = ScanForFont(familyName, style);
        if (def != null)
            _loadedFonts[key] = def;
        return def;
    }

    public FontDefinition? FindFontWithFallback(string familyName, FontStyle style)
    {
        var font = FindFont(familyName, style);
        if (font != null) return font;

        if (_fallbackMap.TryGetValue(familyName, out var fallbacks))
        {
            foreach (var fb in fallbacks)
            {
                font = FindFont(fb, style);
                if (font != null) return font;
            }
        }

        return FindFont("Helvetica", style) ?? FindFont("Arial", style);
    }

    public FontMetrics? GetMetrics(string familyName, FontStyle style, float sizePoints)
    {
        var cacheKey = $"{familyName}:{style}:{sizePoints:F1}";
        if (_metricsCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var fontDef = FindFontWithFallback(familyName, style);
        if (fontDef == null)
        {
            _logger.LogWarning("Font not found: {Family} {Style}, using fallback metrics", familyName, style);
            fontDef = new FontDefinition { FamilyName = "Helvetica", Style = FontStyle.Regular };
        }

        var metrics = EstimateMetrics(fontDef, sizePoints);
        _metricsCache.TryAdd(cacheKey, metrics);
        return metrics;
    }

    public void RegisterFont(string familyName, FontStyle style, byte[] fontData)
    {
        var key = $"{familyName}:{style}";
        if (_loadedFonts.ContainsKey(key))
        {
            _logger.LogDebug("Font already registered: {Family} {Style}", familyName, style);
            return;
        }

        _loadedFonts[key] = new FontDefinition
        {
            FamilyName = familyName,
            Style = style,
            SizePoints = 12f,
            FontData = fontData,
            IsEmbedded = true
        };
        _logger.LogInformation("Registered font from @font-face: {Family} {Style} ({Size} bytes)", familyName, style, fontData.Length);
    }

    public void RegisterDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;
        if (!_searchedDirectories.Add(directory)) return;

        foreach (var file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".ttf" or ".otf" or ".ttc" or ".woff" or ".woff2")
            {
                TryRegisterFontFile(file);
            }
        }
    }

    private void TryRegisterFontFile(string filePath)
    {
        try
        {
            var familyName = Path.GetFileNameWithoutExtension(filePath);
            var style = DetectFontStyle(familyName);

            var key = $"{familyName}:{style}";
            if (!_loadedFonts.ContainsKey(key))
            {
                var data = File.ReadAllBytes(filePath);
                _loadedFonts[key] = new FontDefinition
                {
                    FamilyName = familyName,
                    Style = style,
                    SizePoints = 12f,
                    FilePath = filePath,
                    FontData = data,
                    IsEmbedded = true
                };
                _logger.LogDebug("Registered font: {Family} {Style}", familyName, style);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register font: {Path}", filePath);
        }
    }

    private FontDefinition? ScanForFont(string familyName, FontStyle style)
    {
        foreach (var dir in _searchDirectories)
        {
            if (!Directory.Exists(dir)) continue;
            _searchedDirectories.Add(dir);

            var pattern = $"*{familyName}*";
            try
            {
                foreach (var file in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext is ".ttf" or ".otf" or ".ttc")
                    {
                        var detectedStyle = DetectFontStyle(Path.GetFileNameWithoutExtension(file));
                        if (detectedStyle == style)
                        {
                            var data = File.ReadAllBytes(file);
                            return new FontDefinition
                            {
                                FamilyName = familyName,
                                Style = style,
                                SizePoints = 12f,
                                FilePath = file,
                                FontData = data,
                                IsEmbedded = true
                            };
                        }
                    }
                }
            }
            catch { }
        }
        return null;
    }

    private void RegisterBuiltInFallbacks()
    {
        foreach (var (family, style, pattern) in BuiltInFonts)
        {
            var key = $"{family}:{style}";
            if (!_loadedFonts.ContainsKey(key))
            {
                _loadedFonts[key] = new FontDefinition
                {
                    FamilyName = family,
                    Style = style,
                    SizePoints = 12f,
                    IsEmbedded = false
                };
            }
        }
    }

    private static FontStyle DetectFontStyle(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        bool bold = lower.Contains("bold") || lower.Contains("bd") || lower.Contains("black");
        bool italic = lower.Contains("italic") || lower.Contains("oblique") || lower.Contains("it") || lower.Contains("i");

        if (bold && italic) return FontStyle.BoldItalic;
        if (bold) return FontStyle.Bold;
        if (italic) return FontStyle.Italic;
        return FontStyle.Regular;
    }

    private static FontMetrics EstimateMetrics(FontDefinition fontDef, float sizePoints)
    {
        var scale = sizePoints / 12f;

        return new FontMetrics
        {
            FamilyName = fontDef.FamilyName,
            Style = fontDef.Style,
            SizePoints = sizePoints,
            Ascender = 10f * scale,
            Descender = -3f * scale,
            LineHeight = 13f * scale,
            CapHeight = 8f * scale,
            XHeight = 5f * scale,
            UnderlinePosition = -1.5f * scale,
            UnderlineThickness = 0.5f * scale,
            StrikeoutPosition = 3.5f * scale,
            StrikeoutThickness = 0.5f * scale,
            AdvanceWidths = EstimateAdvanceWidths(fontDef, scale)
        };
    }

    private static Dictionary<char, float> EstimateAdvanceWidths(FontDefinition fontDef, float scale)
    {
        var widths = new Dictionary<char, float>(256);
        bool isMonospace = fontDef.FamilyName.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
                           fontDef.FamilyName.Contains("Mono", StringComparison.OrdinalIgnoreCase);

        float baseWidth = fontDef.FamilyName switch
        {
            string n when n.Contains("Courier", StringComparison.OrdinalIgnoreCase) => 7.2f,
            string n when n.Contains("Times", StringComparison.OrdinalIgnoreCase) => 5.8f,
            _ => 6.4f
        };

        for (char c = ' '; c <= '~'; c++)
        {
            float w = isMonospace ? baseWidth :
                c == ' ' ? baseWidth * 0.6f :
                c == 'W' || c == 'M' ? baseWidth * 1.3f :
                c == 'I' || c == 'l' ? baseWidth * 0.5f :
                c >= 'A' && c <= 'Z' ? baseWidth * (1.0f + (c - 'A') * 0.003f) :
                c >= 'a' && c <= 'z' ? baseWidth * 0.85f :
                char.IsDigit(c) ? baseWidth * 0.85f :
                baseWidth * 0.7f;
            widths[c] = w * scale;
        }

        return widths;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _loadedFonts.Clear();
            _metricsCache.Clear();
            _disposed = true;
        }
    }
}
