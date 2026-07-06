using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.Typography;

namespace PdfEr.Infrastructure.PdfWriters;

public partial class PdfWriter
{
    private int ResolveFontIndex(string? fontFamily, bool bold, bool italic)
    {
        if (_fontRegistry == null)
            return 0;

        var style = (bold, italic) switch
        {
            (true, true) => FontStyle.BoldItalic,
            (true, false) => FontStyle.Bold,
            (false, true) => FontStyle.Italic,
            (false, false) => FontStyle.Regular
        };

        var fontDef = _fontRegistry.FindFontWithFallback(fontFamily ?? "sans-serif", style);
        var family = fontDef?.FamilyName ?? "Helvetica";
        var key = $"{family}:{style}";

        if (_fontKeyToIndex.TryGetValue(key, out var existing))
            return existing;

        var idx = _fontEntries.Count;
        var fontKey = $"/F{idx + 1}";
        _fontKeyToIndex[key] = idx;
        _fontEntries.Add(new FontEntry
        {
            FontKey = fontKey,
            FamilyName = family,
            Style = style,
            FontDef = fontDef
        });
        return idx;
    }

    private static float GetFontSizeFromStyle(CssDeclarationBlock? style, float defaultSize)
    {
        var val = style?.GetPropertyValue("font-size");
        if (val == null) return defaultSize;
        val = val.Trim().ToLowerInvariant();

        if (val.EndsWith("pt")) return float.TryParse(val[..^2], out var v) ? v : defaultSize;
        if (val.EndsWith("px")) return float.TryParse(val[..^2], out var v) ? v * 0.75f : defaultSize;
        if (val.EndsWith("mm")) return float.TryParse(val[..^2], out var v) ? v * 2.8346f : defaultSize;
        if (val.EndsWith("em")) return float.TryParse(val[..^2], out var v) ? v * defaultSize : defaultSize;
        if (val.EndsWith("%")) return float.TryParse(val[..^1], out var v) ? v * defaultSize * 0.01f : defaultSize;
        if (val.EndsWith("rem")) return float.TryParse(val[..^3], out var v) ? v * defaultSize : defaultSize;

        return val switch
        {
            "xx-small" => 7f,
            "x-small" => 7.7f,
            "small" => 8.6f,
            "medium" => 10f,
            "large" => 12f,
            "x-large" => 15f,
            "xx-large" => 20f,
            "smaller" => defaultSize * 0.83f,
            "larger" => defaultSize * 1.2f,
            _ => defaultSize
        };
    }

    private float MeasureTextWidth(string text, string? fontFamily, bool bold, bool italic, float fontSizePt)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        if (_fontRegistry == null)
            return text.Length * fontSizePt * 0.5f;

        var style = (bold, italic) switch
        {
            (true, true) => FontStyle.BoldItalic,
            (true, false) => FontStyle.Bold,
            (false, true) => FontStyle.Italic,
            (false, false) => FontStyle.Regular
        };

        var metrics = _fontRegistry.GetMetrics(fontFamily ?? "sans-serif", style, fontSizePt);
        if (metrics == null || metrics.AdvanceWidths == null)
            return text.Length * fontSizePt * 0.5f;

        float total = 0;
        foreach (char c in text)
            total += GetCharAdvance(metrics, c);
        return total;
    }

    private static float GetCharAdvance(FontMetrics metrics, char c)
    {
        if (metrics.AdvanceWidths.TryGetValue(c, out var w))
            return w;

        return metrics.SizePoints * 0.5f;
    }

    private static float GetLineHeight(CssDeclarationBlock? style, float defaultFactor)
    {
        if (style == null) return defaultFactor;
        var val = style.GetPropertyValue("line-height");
        if (string.IsNullOrWhiteSpace(val)) return defaultFactor;
        val = val.Trim().ToLowerInvariant();

        if (float.TryParse(val, out var fv) && fv > 0)
            return fv;

        if (val.EndsWith("pt") && float.TryParse(val[..^2], out var pt))
            return pt / 12f;
        if (val.EndsWith("em") && float.TryParse(val[..^2], out var em))
            return em;
        if (val.EndsWith("%") && float.TryParse(val[..^1], out var pct))
            return pct / 100f;

        return defaultFactor;
    }
}
