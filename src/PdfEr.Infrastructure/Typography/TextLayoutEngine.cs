using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Typography;

namespace PdfEr.Infrastructure.Typography;

public sealed class TextLayoutEngine : ITextLayoutEngine
{
    private readonly IFontRegistry _fontRegistry;

    public TextLayoutEngine(IFontRegistry fontRegistry)
    {
        _fontRegistry = fontRegistry;
    }

    public TextLayoutResult LayoutText(string text, FontDefinition font, float maxWidth,
        TextAlignment alignment = TextAlignment.Left, float lineSpacing = 1.15f)
    {
        var runs = new List<TextRun>
        {
            new()
            {
                Text = text,
                Font = font,
                SizePoints = font.SizePoints,
                Bold = font.Style == FontStyle.Bold || font.Style == FontStyle.BoldItalic,
                Italic = font.Style == FontStyle.Italic || font.Style == FontStyle.BoldItalic
            }
        };
        return LayoutRichText(runs, maxWidth, alignment, lineSpacing);
    }

    public TextLayoutResult LayoutRichText(IReadOnlyList<TextRun> runs, float maxWidth,
        TextAlignment alignment = TextAlignment.Left, float lineSpacing = 1.15f)
    {
        var result = new TextLayoutResult
        {
            TotalWidth = maxWidth,
            Overflow = false
        };

        var currentLine = new TextLine { Alignment = alignment, LineSpacing = lineSpacing };
        float currentX = 0;
        float currentY = 0;
        float maxLineHeight = 0;
        var wordBuffer = new List<(char c, float w, TextRun run)>();

        foreach (var run in runs)
        {
            var metrics = _fontRegistry.GetMetrics(run.Font.FamilyName,
                ConvertStyle(run.Bold, run.Italic), run.SizePoints);

            if (metrics == null) continue;

            float spaceWidth = GetAdvanceWidth(metrics, ' ');

            for (int i = 0; i < run.Text.Length; i++)
            {
                char c = run.Text[i];
                float charWidth = GetAdvanceWidth(metrics, c);

                if (c == '\n')
                {
                    FlushWordBuffer(wordBuffer, currentLine, ref currentX, ref maxLineHeight);
                    finalizeLine(currentLine, result, ref currentY, ref currentX, ref maxLineHeight, maxWidth);
                    wordBuffer.Clear();
                    continue;
                }

                if (c == ' ' || c == '\t')
                {
                    FlushWordBuffer(wordBuffer, currentLine, ref currentX, ref maxLineHeight);

                    float wordWidth = spaceWidth;
                    if (currentX + wordWidth > maxWidth && currentLine.Runs.Count > 0)
                    {
                        finalizeLine(currentLine, result, ref currentY, ref currentX, ref maxLineHeight, maxWidth);
                    }

                    currentLine.Runs.Add(new TextRun
                    {
                        Text = " ",
                        Font = run.Font,
                        SizePoints = run.SizePoints,
                        Bold = run.Bold,
                        Italic = run.Italic,
                        Color = run.Color
                    });
                    currentX += wordWidth;
                    maxLineHeight = Math.Max(maxLineHeight, metrics.LineHeight);
                    continue;
                }

                wordBuffer.Add((c, charWidth, run));
            }
        }

        FlushWordBuffer(wordBuffer, currentLine, ref currentX, ref maxLineHeight);

        if (currentLine.Runs.Count > 0)
        {
            currentLine.Width = currentX;
            currentLine.Height = maxLineHeight;
            currentLine.Y = currentY;
            finalizeAlignedX(currentLine, maxWidth);
            result.Lines.Add(currentLine);
            currentY += maxLineHeight;
        }

        result.TotalHeight = currentY;
        result.CharacterCount = runs.Sum(r => r.Text.Length);
        return result;
    }

    public float MeasureTextWidth(string text, FontDefinition font)
    {
        var metrics = _fontRegistry.GetMetrics(font.FamilyName, font.Style, font.SizePoints);
        if (metrics == null) return 0;

        float total = 0;
        foreach (char c in text)
            total += GetAdvanceWidth(metrics, c);
        return total;
    }

    public int GetCharacterIndexAtPosition(string text, FontDefinition font, float xPosition)
    {
        var metrics = _fontRegistry.GetMetrics(font.FamilyName, font.Style, font.SizePoints);
        if (metrics == null) return 0;

        float accum = 0;
        for (int i = 0; i < text.Length; i++)
        {
            accum += GetAdvanceWidth(metrics, text[i]);
            if (accum > xPosition) return i;
        }
        return text.Length;
    }

    private void finalizeLine(TextLine line, TextLayoutResult result,
        ref float y, ref float x, ref float maxHeight, float maxWidth)
    {
        line.Width = x;
        line.Height = maxHeight;
        line.Y = y;
        finalizeAlignedX(line, maxWidth);
        result.Lines.Add(line);

        y += maxHeight * line.LineSpacing;
        x = 0;
        maxHeight = 0;
    }

    private static void finalizeAlignedX(TextLine line, float maxWidth)
    {
        if (line.Alignment == TextAlignment.Left) return;

        float totalWidth = line.Runs.Sum(r => MeasureRunWidth(r));
        float offset = line.Alignment switch
        {
            TextAlignment.Center => (maxWidth - totalWidth) / 2f,
            TextAlignment.Right => maxWidth - totalWidth,
            TextAlignment.Justify => maxWidth - totalWidth,
            _ => 0
        };

        line.X = offset;
        line.Width = totalWidth;
    }

    private static float MeasureRunWidth(TextRun run)
    {
        float w = 0;
        foreach (char c in run.Text)
            w += run.Font.SizePoints * 0.5f;
        return w;
    }

    private static void FlushWordBuffer(List<(char c, float w, TextRun run)> buffer,
        TextLine line, ref float currentX, ref float maxLineHeight)
    {
        if (buffer.Count == 0) return;

        var word = new string(buffer.Select(b => b.c).ToArray());
        float wordWidth = buffer.Sum(b => b.w);
        var firstRun = buffer[0].run;

        line.Runs.Add(new TextRun
        {
            Text = word,
            Font = firstRun.Font,
            SizePoints = firstRun.SizePoints,
            Bold = firstRun.Bold,
            Italic = firstRun.Italic,
            Color = firstRun.Color
        });

        currentX += wordWidth;
        buffer.Clear();
    }

    private static float GetAdvanceWidth(FontMetrics metrics, char c)
    {
        return metrics.AdvanceWidths.TryGetValue(c, out var w) ? w : metrics.AdvanceWidths.GetValueOrDefault('n', 6f);
    }

    private static FontStyle ConvertStyle(bool bold, bool italic)
    {
        if (bold && italic) return FontStyle.BoldItalic;
        if (bold) return FontStyle.Bold;
        if (italic) return FontStyle.Italic;
        return FontStyle.Regular;
    }
}
