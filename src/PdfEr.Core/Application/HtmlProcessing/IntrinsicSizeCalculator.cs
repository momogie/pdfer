using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.Typography;

namespace PdfEr.Core.Application.HtmlProcessing;

/// <summary>
/// Pass 1 (intrinsic sizing) of the box-tree layout pipeline
/// (docs/plans/phase-01-foundation.md). Computes min-content/max-content width
/// for every box in a <see cref="LayoutBox"/> tree, bottom-up, using real font
/// metrics instead of the streaming pipeline's <c>textLen * fontSize * 0.5f</c>
/// guess (LayoutEngine.cs inline-block shrink-to-fit width).
///
/// Word-boundary splitting (whitespace) approximates CSS's min-content "widest
/// unbreakable unit" — full UAX#14 line-breaking is Phase 3 (text) work, not
/// this pass. Results feed Pass 2 (placement, not yet implemented): shrink-to-fit
/// width, auto table column widths, and flex-basis all need this number.
/// </summary>
public sealed class IntrinsicSizeCalculator
{
    private readonly IFontRegistry _fontRegistry;
    private const float PtToMm = 0.3528f;

    public IntrinsicSizeCalculator(IFontRegistry fontRegistry)
    {
        _fontRegistry = fontRegistry;
    }

    /// <summary>Computes and stores IntrinsicSizes on this box and every descendant, bottom-up.</summary>
    public IntrinsicSizes Calculate(LayoutBox box)
    {
        var sizes = box.Kind switch
        {
            LayoutBoxKind.Text => MeasureText(box),
            _ => CalculateContainer(box),
        };

        box.Intrinsic = sizes;
        return sizes;
    }

    private IntrinsicSizes MeasureText(LayoutBox box)
    {
        var text = box.TextContent ?? "";
        if (text.Length == 0) return new IntrinsicSizes(0, 0);

        var fontFamily = box.Style.GetPropertyValue("font-family") ?? "DejaVu Sans";
        var fontStyle = ResolveFontStyle(box.Style);
        var metrics = _fontRegistry.GetMetrics(fontFamily, fontStyle, box.Style.FontSizePt);

        // No metrics available (unregistered font): fall back to the same
        // heuristic the streaming pipeline already uses elsewhere, converted
        // to mm, rather than silently returning zero-width intrinsic sizes.
        if (metrics == null)
        {
            var approx = text.Length * box.Style.FontSizeMm * 0.5f;
            var longestWordApprox = text.Split(' ', '\t', '\n')
                .Where(w => w.Length > 0)
                .Select(w => w.Length * box.Style.FontSizeMm * 0.5f)
                .DefaultIfEmpty(0)
                .Max();
            return new IntrinsicSizes(longestWordApprox, approx);
        }

        float maxContentPt = 0;
        float minContentPt = 0;
        float currentWordPt = 0;

        foreach (var ch in text)
        {
            var advance = GetAdvanceWidth(metrics, ch);
            maxContentPt += advance;

            if (char.IsWhiteSpace(ch))
            {
                minContentPt = Math.Max(minContentPt, currentWordPt);
                currentWordPt = 0;
            }
            else
            {
                currentWordPt += advance;
            }
        }
        minContentPt = Math.Max(minContentPt, currentWordPt);

        return new IntrinsicSizes(minContentPt * PtToMm, maxContentPt * PtToMm);
    }

    private IntrinsicSizes CalculateContainer(LayoutBox box)
    {
        if (box.Children.Count == 0)
            return new IntrinsicSizes(0, 0);

        var childSizes = box.Children.Select(Calculate).ToList();

        IntrinsicSizes contentSizes = box.Kind is LayoutBoxKind.Inline or LayoutBoxKind.Anonymous
            // Inline formatting context: children flow on the same line(s), so
            // max-content is their sum; min-content is still the single widest
            // unbreakable word among them (a block-level child inside an anonymous
            // box would force a break, but that mixed case isn't produced by
            // BoxTreeBuilder today, so it's out of scope for this pass).
            ? new IntrinsicSizes(
                childSizes.Count > 0 ? childSizes.Max(s => s.MinContentWidth) : 0,
                childSizes.Sum(s => s.MaxContentWidth))
            // Block formatting context: each child stacks on its own line, so the
            // container's intrinsic width is the widest child, for both min and max.
            : new IntrinsicSizes(
                childSizes.Count > 0 ? childSizes.Max(s => s.MinContentWidth) : 0,
                childSizes.Count > 0 ? childSizes.Max(s => s.MaxContentWidth) : 0);

        var (paddingBorder, _) = BoxModelAdditions(box);
        return new IntrinsicSizes(
            contentSizes.MinContentWidth + paddingBorder,
            contentSizes.MaxContentWidth + paddingBorder);
    }

    /// <summary>
    /// Padding + border width (left + right), which intrinsic sizing adds on top
    /// of content width per CSS Intrinsic and Extrinsic Sizing. Margins are
    /// deliberately excluded — callers add margin when computing used width,
    /// consistent with how shrink-to-fit is defined against the border box.
    /// </summary>
    private static (float paddingBorder, float margin) BoxModelAdditions(LayoutBox box)
    {
        var style = box.Style;
        float ParseMm(string? propertyName)
        {
            var value = style.GetPropertyValue(propertyName ?? "");
            return CssLengthParser.ParseLengthMm(value);
        }

        var paddingLeft = ParseMm("padding-left");
        var paddingRight = ParseMm("padding-right");
        var borderLeft = ParseMm("border-left-width");
        var borderRight = ParseMm("border-right-width");
        var marginLeft = ParseMm("margin-left");
        var marginRight = ParseMm("margin-right");

        return (paddingLeft + paddingRight + borderLeft + borderRight, marginLeft + marginRight);
    }

    private static float GetAdvanceWidth(FontMetrics metrics, char c) =>
        metrics.AdvanceWidths.TryGetValue(c, out var w) ? w : metrics.AdvanceWidths.GetValueOrDefault('n', 6f);

    private static FontStyle ResolveFontStyle(ComputedStyle style)
    {
        var weight = style.GetPropertyValue("font-weight");
        var fontStyleValue = style.GetPropertyValue("font-style");

        bool bold = weight is "bold" or "700" or "800" or "900" ||
            (weight != null && int.TryParse(weight, out var w) && w >= 700);
        bool italic = fontStyleValue is "italic" or "oblique";

        if (bold && italic) return FontStyle.BoldItalic;
        if (bold) return FontStyle.Bold;
        if (italic) return FontStyle.Italic;
        return FontStyle.Regular;
    }
}
