using System.Text;
using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.ValueObjects;

namespace PdfEr.Infrastructure.PdfWriters;

public partial class PdfWriter
{
    private void WriteSimpleTextBlock(StringBuilder sb, string text, InlineBox? decoInline,
        float contentWidthPt, float lineHeightPt, float blockXPt, float blockYPt,
        float fontSizePt, string? textAlign, string? fontFamily, bool bold, bool italic, int fontIdx,
        CssDeclarationBlock? style, ColorParser colorParser,
        int pageNumber = 1, int totalPages = 1)
    {
        text = text.Replace("{PAGE_NUM}", pageNumber.ToString())
                   .Replace("{PAGE_COUNT}", totalPages.ToString());

        var lines = new List<string>();
        string? whiteSpace = style?.GetPropertyValue("white-space");
        bool isPre = whiteSpace is "pre";
        bool isPreWrap = whiteSpace is "pre-wrap";
        bool isPreLine = whiteSpace is "pre-line";
        bool isNowrap = whiteSpace is "nowrap";

        if (isPre)
        {
            var rawLines = text.Split('\n');
            foreach (var rawLine in rawLines)
                lines.Add(rawLine);
        }
        else if (isNowrap)
        {
            var collapsed = string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            lines.Add(collapsed);
        }
        else if (isPreWrap)
        {
            var rawLines = text.Split('\n');
            foreach (var rawLine in rawLines)
                WrapLine(rawLine, lines, contentWidthPt, fontFamily, bold, italic, fontSizePt, false);
        }
        else if (isPreLine)
        {
            var rawLines = text.Split('\n');
            foreach (var rawLine in rawLines)
            {
                var collapsed = string.Join(" ", rawLine.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (!string.IsNullOrEmpty(collapsed))
                    WrapLine(collapsed, lines, contentWidthPt, fontFamily, bold, italic, fontSizePt, false);
                else
                    lines.Add("");
            }
        }
        else
        {
            if (contentWidthPt > 4f)
            {
                var collapsed = string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                WrapLine(collapsed, lines, contentWidthPt, fontFamily, bold, italic, fontSizePt, true);
            }
            else
            {
                var collapsed = string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                lines.Add(collapsed);
            }
        }

        if (lines.Count == 0)
            lines.Add(text);

        sb.Append("BT\n");
        sb.AppendLine($"{_fontEntries[fontIdx].FontKey} {fontSizePt:F2} Tf");

        float textIndentPt = 0;
        var indentVal = style?.GetPropertyValue("text-indent");
        if (!string.IsNullOrWhiteSpace(indentVal))
        {
            if (indentVal.EndsWith("mm") && float.TryParse(indentVal[..^2], out var indentMm))
                textIndentPt = indentMm * MmToPt;
            else if (indentVal.EndsWith("pt") && float.TryParse(indentVal[..^2], out var indentPt))
                textIndentPt = indentPt;
            else if (indentVal.EndsWith("px") && float.TryParse(indentVal[..^2], out var indentPx))
                textIndentPt = indentPx * 0.75f;
            else if (indentVal.EndsWith("em") && float.TryParse(indentVal[..^2], out var indentEm))
                textIndentPt = indentEm * fontSizePt;
            else if (float.TryParse(indentVal, out var indentRaw))
                textIndentPt = indentRaw * fontSizePt;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            float lineWidth = MeasureTextWidth(line, fontFamily, bold, italic, fontSizePt);

            float lineOffsetX = i == 0 ? textIndentPt : 0;
            float lineWordSpacing = 0;

            if (textAlign == "center")
                lineOffsetX = Math.Max(0, (contentWidthPt - lineWidth) / 2);
            else if (textAlign == "right")
                lineOffsetX = Math.Max(0, contentWidthPt - lineWidth);
            else if (textAlign == "justify" && (i < lines.Count - 1 || lines.Count == 1) && line.Contains(' '))
            {
                int wc = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (wc > 1)
                {
                    float extra = contentWidthPt - lineWidth;
                    if (extra > 0)
                        lineWordSpacing = extra / (wc - 1);
                }
            }

            float lineY = blockYPt - i * lineHeightPt;

            if (i == 0)
                sb.AppendLine($"{blockXPt + lineOffsetX:F2} {lineY:F2} Td");
            else
                sb.AppendLine($"1 0 0 1 {blockXPt + lineOffsetX:F2} {lineY:F2} Tm");

            if (lineWordSpacing > 0)
                sb.AppendLine($"{lineWordSpacing:F2} Tw");

            if (!string.IsNullOrEmpty(line))
                sb.AppendLine($"{FormatPdfText(line)} Tj");

            if (lineWordSpacing > 0)
                sb.AppendLine("0 Tw");
        }

        sb.Append("ET\n");

        var decoStyle = decoInline?.ComputedStyle ?? style;
        string? decoration = null;
        if (decoStyle != null)
            decoration = decoStyle.GetPropertyValue("text-decoration-line");

        if (!string.IsNullOrWhiteSpace(decoration) && decoration != "none")
        {
            string? dcStr = null;
            if (decoStyle != null)
                dcStr = decoStyle.GetPropertyValue("color");
            if (!string.IsNullOrWhiteSpace(dcStr) && dcStr != "transparent" && dcStr != "rgba(0, 0, 0, 0)")
            {
                if (colorParser.TryParse(dcStr, out var dc) && dc is RgbColor drgb && drgb.A > 0)
                    sb.AppendLine($"{drgb.R / 255f:F2} {drgb.G / 255f:F2} {drgb.B / 255f:F2} RG");
                else
                    sb.AppendLine("0 0 0 RG");
            }
            else
                sb.AppendLine("0 0 0 RG");

            sb.AppendLine($"{Math.Max(0.4f, fontSizePt * 0.05f):F2} w");

            for (int i = 0; i < lines.Count; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                float lineW = MeasureTextWidth(lines[i], fontFamily, bold, italic, fontSizePt);
                float lineYDeco = blockYPt - i * lineHeightPt;

                float lineLeftPt = blockXPt;
                if (textAlign == "center")
                    lineLeftPt += Math.Max(0, (contentWidthPt - lineW) / 2);
                else if (textAlign == "right")
                    lineLeftPt += Math.Max(0, contentWidthPt - lineW);

                if (decoration.Contains("underline"))
                {
                    float uy = lineYDeco - fontSizePt * 0.05f;
                    sb.AppendLine($"{lineLeftPt:F2} {uy:F2} m {lineLeftPt + lineW:F2} {uy:F2} l S");
                }
                if (decoration.Contains("line-through"))
                {
                    float ty = lineYDeco + fontSizePt * 0.4f;
                    sb.AppendLine($"{lineLeftPt:F2} {ty:F2} m {lineLeftPt + lineW:F2} {ty:F2} l S");
                }
                if (decoration.Contains("overline"))
                {
                    float oy = lineYDeco + fontSizePt * 0.85f;
                    sb.AppendLine($"{lineLeftPt:F2} {oy:F2} m {lineLeftPt + lineW:F2} {oy:F2} l S");
                }
            }
        }
    }

    private void WriteInlineContentBlock(StringBuilder sb, BlockBox block,
        float contentWidthPt, float blockXPt, float blockYPt,
        float fontSizePt, string? textAlign, int fontIdx,
        float marginLeftPt, float marginTopPt, float pageH, ColorParser colorParser,
        int pageNumber = 1, int totalPages = 1)
    {
        var pageNumStr = pageNumber.ToString();
        var totalPagesStr = totalPages.ToString();
        foreach (var inline in block.InlineContent)
        {
            if (inline.Type == InlineBoxType.Text && inline.Text != null)
            {
                inline.Text = inline.Text.Replace("{PAGE_NUM}", pageNumStr)
                                         .Replace("{PAGE_COUNT}", totalPagesStr);
            }
        }

        float textOffsetX = 0;
        float extraWordSpacing = 0;

        if (textAlign is "center" or "right" or "justify")
        {
            float textWidthPt = 0;
            int wordCount = 0;
            foreach (var inline in block.InlineContent)
            {
                if (inline.Type == InlineBoxType.Text && inline.Text != null)
                {
                    textWidthPt += inline.Text.Length * fontSizePt * 0.5f;
                    wordCount += inline.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                }
            }

            if (textAlign == "center")
                textOffsetX = Math.Max(0, (contentWidthPt - textWidthPt) / 2);
            else if (textAlign == "right")
                textOffsetX = Math.Max(0, contentWidthPt - textWidthPt);
            else if (textAlign == "justify" && wordCount > 1)
            {
                float extraSpace = contentWidthPt - textWidthPt;
                if (extraSpace > 0)
                    extraWordSpacing = extraSpace / (wordCount - 1);
            }
        }

        foreach (var inline in block.InlineContent)
        {
            if (inline.Type != InlineBoxType.Text || inline.Text == null)
                continue;

            var bgStyle = inline.ComputedStyle ?? block.ComputedStyle;
            string? ibg = null;
            if (bgStyle != null)
                ibg = bgStyle.GetPropertyValue("background-color");
            if (string.IsNullOrWhiteSpace(ibg) || ibg == "transparent" || ibg == "rgba(0, 0, 0, 0)")
                continue;

            if (colorParser.TryParse(ibg, out var bgDocColor) && bgDocColor is RgbColor bgRgb && bgRgb.A > 0)
            {
                float ibgLeft = inline.X * MmToPt + marginLeftPt;
                float ibgBottom = pageH - ((inline.Y + inline.Height) * MmToPt) - marginTopPt;
                float ibgWidth = inline.Width * MmToPt;
                float ibgHeight = inline.Height * MmToPt;

                sb.AppendLine($"{bgRgb.R / 255f:F2} {bgRgb.G / 255f:F2} {bgRgb.B / 255f:F2} rg");
                sb.AppendLine($"{ibgLeft:F2} {ibgBottom:F2} {ibgWidth:F2} {ibgHeight:F2} re f");
            }
        }

        sb.Append("BT\n");
        sb.AppendLine($"{_fontEntries[fontIdx].FontKey} {fontSizePt:F2} Tf");

        if (extraWordSpacing > 0)
            sb.AppendLine($"{extraWordSpacing:F2} Tw");

        sb.AppendLine($"{blockXPt + textOffsetX:F2} {blockYPt:F2} Td");

        foreach (var inline in block.InlineContent)
        {
            if (inline.Type == InlineBoxType.Text && inline.Text != null)
                sb.AppendLine($"{FormatPdfText(inline.Text)} Tj");
        }

        sb.Append("ET\n");

        if (extraWordSpacing > 0)
            sb.AppendLine("0 Tw");

        foreach (var inline in block.InlineContent)
        {
            if (inline.Type != InlineBoxType.Text || inline.Text == null)
                continue;

            var inlineStyle = inline.ComputedStyle ?? block.ComputedStyle;
            string? inlineDecoration = null;
            if (inlineStyle != null)
                inlineDecoration = inlineStyle.GetPropertyValue("text-decoration-line");

            if (string.IsNullOrWhiteSpace(inlineDecoration) || inlineDecoration == "none")
                continue;

            float inlineXPt = inline.X * MmToPt + marginLeftPt;
            float inlineYPt = pageH - (inline.Y * MmToPt) - marginTopPt;
            float inlineWidthPt = inline.Text.Length * fontSizePt * 0.5f;

            string? decoColorStr = null;
            if (inlineStyle != null)
                decoColorStr = inlineStyle.GetPropertyValue("color");
            if (!string.IsNullOrWhiteSpace(decoColorStr) && decoColorStr != "transparent" && decoColorStr != "rgba(0, 0, 0, 0)")
            {
                if (colorParser.TryParse(decoColorStr, out var dc) && dc is RgbColor decoRgb && decoRgb.A > 0)
                    sb.AppendLine($"{decoRgb.R / 255f:F2} {decoRgb.G / 255f:F2} {decoRgb.B / 255f:F2} RG");
                else
                    sb.AppendLine("0 0 0 RG");
            }
            else
                sb.AppendLine("0 0 0 RG");

            sb.AppendLine($"{Math.Max(0.4f, fontSizePt * 0.05f):F2} w");

            if (inlineDecoration.Contains("underline"))
            {
                float uy = inlineYPt - fontSizePt * 0.05f;
                sb.AppendLine($"{inlineXPt:F2} {uy:F2} m {inlineXPt + inlineWidthPt:F2} {uy:F2} l S");
            }
            if (inlineDecoration.Contains("line-through"))
            {
                float ty = inlineYPt + fontSizePt * 0.4f;
                sb.AppendLine($"{inlineXPt:F2} {ty:F2} m {inlineXPt + inlineWidthPt:F2} {ty:F2} l S");
            }
            if (inlineDecoration.Contains("overline"))
            {
                float oy = inlineYPt + fontSizePt * 0.85f;
                sb.AppendLine($"{inlineXPt:F2} {oy:F2} m {inlineXPt + inlineWidthPt:F2} {oy:F2} l S");
            }
        }
    }

    private void WrapLine(string text, List<string> lines, float contentWidthPt,
        string? fontFamily, bool bold, bool italic, float fontSizePt, bool collapseSpaces)
    {
        var words = text.Split(' ', collapseSpaces ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None);
        var curLine = new List<string>();
        float curWidth = 0;
        float spaceWidth = MeasureTextWidth(" ", fontFamily, bold, italic, fontSizePt);

        foreach (var word in words)
        {
            float wordWidth = MeasureTextWidth(word, fontFamily, bold, italic, fontSizePt);
            if (curLine.Count > 0 && curWidth + spaceWidth + wordWidth > contentWidthPt)
            {
                lines.Add(string.Join(" ", curLine));
                curLine.Clear();
                curWidth = 0;
            }
            if (curLine.Count > 0)
                curWidth += spaceWidth;
            curLine.Add(word);
            curWidth += wordWidth;
        }
        if (curLine.Count > 0)
            lines.Add(string.Join(" ", curLine));
    }

    private static float ParseBorderRadius(CssDeclarationBlock? style)
    {
        if (style == null) return 0;
        var val = style.GetPropertyValue("border-radius");
        if (string.IsNullOrWhiteSpace(val)) return 0;
        val = val.Trim().ToLowerInvariant();
        if (val.EndsWith("mm") && float.TryParse(val[..^2], out var mm)) return mm * MmToPt;
        if (val.EndsWith("pt") && float.TryParse(val[..^2], out var pt)) return pt;
        if (val.EndsWith("px") && float.TryParse(val[..^2], out var px)) return px * 0.75f;
        if (val.EndsWith("em") && float.TryParse(val[..^2], out var em)) return em * 12f;
        if (float.TryParse(val, out var raw)) return raw * MmToPt;
        return 0;
    }

    private static void AppendRoundedRectPath(StringBuilder sb, float x, float y, float w, float h, float r, string op)
    {
        float maxR = Math.Min(w, h) / 2f;
        if (r > maxR) r = maxR;
        if (r <= 0)
        {
            sb.AppendLine($"{x:F2} {y:F2} {w:F2} {h:F2} re {op}");
            return;
        }

        const float k = 0.5522847498f;
        float kR = k * r;

        sb.AppendLine($"{x + r:F2} {y:F2} m");
        sb.AppendLine($"{x + w - r:F2} {y:F2} l");
        sb.AppendLine($"{x + w - r + kR:F2} {y:F2} {x + w:F2} {y + r - kR:F2} {x + w:F2} {y + r:F2} c");
        sb.AppendLine($"{x + w:F2} {y + h - r:F2} l");
        sb.AppendLine($"{x + w:F2} {y + h - r + kR:F2} {x + w - r + kR:F2} {y + h:F2} {x + w - r:F2} {y + h:F2} c");
        sb.AppendLine($"{x + r:F2} {y + h:F2} l");
        sb.AppendLine($"{x + r - kR:F2} {y + h:F2} {x:F2} {y + h - r + kR:F2} {x:F2} {y + h - r:F2} c");
        sb.AppendLine($"{x:F2} {y + r:F2} l");
        sb.AppendLine($"{x:F2} {y + r - kR:F2} {x + r - kR:F2} {y:F2} {x + r:F2} {y:F2} c");

        if (op == "S")
            sb.AppendLine("h S");
        else
            sb.AppendLine("h f");
    }

    private static bool ShouldShowHeaderFooter(int pageNumber, HeaderFooterBox hf)
    {
        if (pageNumber == 1 && !hf.ShowOnFirstPage) return false;
        if (pageNumber % 2 == 0 && !hf.ShowOnEvenPages) return false;
        if (pageNumber % 2 == 1 && pageNumber > 1 && !hf.ShowOnOddPages) return false;
        return true;
    }
}
