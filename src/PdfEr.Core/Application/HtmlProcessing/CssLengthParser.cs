namespace PdfEr.Core.Application.HtmlProcessing;

/// <summary>
/// Single source of truth for parsing a raw CSS length string (e.g. "10px",
/// "2mm", "thin") into millimetres (the box-tree pipeline's and LayoutEngine's
/// shared layout unit, see memory [[pdfer-layout-units]]).
///
/// Before this existed, the exact same unit table was hand-copied three times:
/// LayoutEngine.ParseLength, IntrinsicSizeCalculator.ParseLengthMm, and
/// BlockPlacer.ParseLengthMm. They were kept byte-for-byte identical by
/// convention, but nothing enforced that -- a future edit to one could silently
/// diverge from the others. This consolidates them into one method all three
/// now delegate to, with the exact same constants (0.3528f for pt->mm etc., not
/// the slightly different ratio IUnitConverter derives from 25.4/72) so no
/// existing numeric output changes.
///
/// Deliberately NOT routed through IUnitConverter: that interface converts
/// already-typed UnitOfMeasure values, not raw CSS strings, and is a DI-injected
/// service with different semantics (e.g. throws for unsupported units rather
/// than defaulting to 0). Unifying the two would be a larger, separate change;
/// this only removes the duplication that existed for certain.
/// </summary>
public static class CssLengthParser
{
    /// <summary>Parses an absolute CSS length (no percentages) to millimetres.</summary>
    public static float ParseLengthMm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        value = value.Trim().ToLowerInvariant();
        if (value is "0" or "0px" or "0pt" or "0mm" or "auto" or "none") return 0;

        if (value is "thin") return 0.5f;
        if (value is "medium") return 1f;
        if (value is "thick") return 2f;

        if (value.EndsWith("mm")) return float.TryParse(value[..^2], out var v) ? v : 0;
        if (value.EndsWith("pt")) return float.TryParse(value[..^2], out var v) ? v * 0.3528f : 0;
        if (value.EndsWith("px")) return float.TryParse(value[..^2], out var v) ? v * 0.2646f : 0;
        if (value.EndsWith("cm")) return float.TryParse(value[..^2], out var v) ? v * 10f : 0;
        if (value.EndsWith("in")) return float.TryParse(value[..^2], out var v) ? v * 25.4f : 0;
        if (value.EndsWith("rem")) return float.TryParse(value[..^3], out var v) ? v * 10f * 0.3528f : 0;
        if (value.EndsWith("em")) return float.TryParse(value[..^2], out var v) ? v * 10f * 0.3528f : 0;

        if (float.TryParse(value, out var num)) return num;
        return 0;
    }

    /// <summary>Parses a CSS length that may be a percentage of <paramref name="parentDimension"/>, to millimetres.</summary>
    public static float ParseCssLengthMm(string? value, float parentDimension)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        value = value.Trim().ToLowerInvariant();
        if (value == "auto") return 0;

        if (value.EndsWith('%'))
        {
            return float.TryParse(value[..^1].Trim(), out var pct) ? parentDimension * pct / 100f : 0;
        }

        return ParseLengthMm(value);
    }
}
