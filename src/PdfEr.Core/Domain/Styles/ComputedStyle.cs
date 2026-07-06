namespace PdfEr.Core.Domain.Styles;

/// <summary>
/// A style-resolution-stage result: the cascaded <see cref="CssDeclarationBlock"/>
/// (selector matching + inheritance + shorthand expansion already applied by
/// CssMerger/CssNormalizer) plus a handful of values eagerly resolved to absolute
/// units, computed once per box instead of re-parsed at every layout call site.
///
/// This does NOT attempt to resolve every CSS property to an absolute value up
/// front (that would duplicate the property table CssMerger/CssNormalizer already
/// own). It only pre-resolves the values LayoutEngine currently recomputes ad-hoc
/// on every call (GetFontSize/GetFontSizePt, ParseLength) so Pass 1/Pass 2 box-tree
/// layout have a single source of truth for them. Percentage-valued box-model
/// properties (width/height/margin/padding as %) still need the containing block
/// and are resolved by <see cref="Layout.ContainingBlock"/>-aware code in the
/// layout passes, not here.
/// </summary>
public sealed class ComputedStyle
{
    /// <summary>The raw cascaded declarations this ComputedStyle was resolved from.</summary>
    public CssDeclarationBlock Declarations { get; }

    /// <summary>Font size in points — the unit PDF's Tf text operator expects.</summary>
    public float FontSizePt { get; }

    /// <summary>
    /// Font size in millimetres — the unit the layout engine's box geometry uses.
    /// See memory [[pdfer-layout-units]]: mixing these up made every line-height
    /// ~2.83x too tall in the streaming pipeline.
    /// </summary>
    public float FontSizeMm { get; }

    public string Display { get; }
    public string Position { get; }
    public string? Float { get; }

    private ComputedStyle(CssDeclarationBlock declarations, float fontSizePt, float fontSizeMm,
        string display, string position, string? @float)
    {
        Declarations = declarations;
        FontSizePt = fontSizePt;
        FontSizeMm = fontSizeMm;
        Display = display;
        Position = position;
        Float = @float;
    }

    public string? GetPropertyValue(string propertyName) => Declarations.GetPropertyValue(propertyName);

    public static ComputedStyle Resolve(CssDeclarationBlock declarations)
    {
        var fontSizePt = ResolveFontSizePt(declarations.GetPropertyValue("font-size"));
        var fontSizeMm = fontSizePt * PtToMm;

        var display = declarations.GetPropertyValue("display")?.Trim().ToLowerInvariant() ?? "inline";
        var position = declarations.GetPropertyValue("position")?.Trim().ToLowerInvariant() ?? "static";
        var floatValue = declarations.GetPropertyValue("float")?.Trim().ToLowerInvariant();
        if (floatValue == "none") floatValue = null;

        return new ComputedStyle(declarations, fontSizePt, fontSizeMm, display, position, floatValue);
    }

    private const float PtToMm = 0.3528f;

    // Mirrors LayoutEngine.GetFontSizePt exactly (same keyword table and unit
    // conversions) so box-tree and streaming layout agree on font sizing while
    // both pipelines coexist behind the UseBoxTreeLayout flag.
    private static float ResolveFontSizePt(string? val)
    {
        if (val == null) return 10f;
        val = val.Trim().ToLowerInvariant();

        if (val.EndsWith("pt")) return float.TryParse(val[..^2], out var v) ? v : 10f;
        if (val.EndsWith("px")) return float.TryParse(val[..^2], out var v) ? v * 0.75f : 10f;
        if (val.EndsWith("mm")) return float.TryParse(val[..^2], out var v) ? v * 2.8346f : 10f;
        if (val.EndsWith("rem")) return float.TryParse(val[..^3], out var v) ? v * 10f : 10f;
        if (val.EndsWith("em")) return float.TryParse(val[..^2], out var v) ? v * 10f : 10f;
        if (val.EndsWith("%")) return float.TryParse(val[..^1], out var v) ? v * 0.1f : 10f;

        return val switch
        {
            "xx-small" => 7f,
            "x-small" => 7.7f,
            "small" => 8.6f,
            "medium" => 10f,
            "large" => 12f,
            "x-large" => 15f,
            "xx-large" => 20f,
            "smaller" => 8.3f,
            "larger" => 12f,
            _ => 10f
        };
    }
}
