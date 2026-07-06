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

            // Pre-compute UAX#14 break opportunities for this run's text
            var runBreaks = new HashSet<int>(Uax14LineBreaker.GetBreakOpportunities(run.Text));

            for (int i = 0; i < run.Text.Length; i++)
            {
                char c = run.Text[i];
                float charWidth = GetAdvanceWidth(metrics, c);

                // Mandatory line breaks (BK/LF/CR/NL)
                if (c == '\n' || c == '\r')
                {
                    FlushWordBuffer(wordBuffer, currentLine, ref currentX, ref maxLineHeight);
                    finalizeLine(currentLine, result, ref currentY, ref currentX, ref maxLineHeight, maxWidth);
                    wordBuffer.Clear();
                    continue;
                }

                // UAX#14 break opportunity: flush accumulated word buffer,
                // then handle the break character itself.
                // Break at position i means break is allowed BEFORE char i.
                // We check AFTER processing the character at i-1 and BEFORE
                // processing char i — i.e., when we're at position i, check
                // if a break is allowed after position i-1 (i.e., break pos i).
                if (runBreaks.Contains(i) && wordBuffer.Count > 0)
                {
                    FlushWordBuffer(wordBuffer, currentLine, ref currentX, ref maxLineHeight);

                    // If this position is also a space/tab, treat as normal space
                    if (c == ' ' || c == '\t')
                    {
                        float breakWidth = spaceWidth;
                        if (currentX + breakWidth > maxWidth && currentLine.Runs.Count > 0)
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
                        currentX += breakWidth;
                        maxLineHeight = Math.Max(maxLineHeight, metrics.LineHeight);
                        continue;
                    }
                }

                // For spaces at non-break positions (shouldn't normally happen),
                // flush and add the space run
                if (c == ' ' || c == '\t')
                {
                    FlushWordBuffer(wordBuffer, currentLine, ref currentX, ref maxLineHeight);

                    float breakWidth = spaceWidth;
                    if (currentX + breakWidth > maxWidth && currentLine.Runs.Count > 0)
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
                    currentX += breakWidth;
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

    private void finalizeAlignedX(TextLine line, float maxWidth)
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

    private float MeasureRunWidth(TextRun run)
    {
        var metrics = _fontRegistry.GetMetrics(run.Font.FamilyName,
            run.Font.Style, run.SizePoints);
        if (metrics == null)
            return run.Text.Length * run.SizePoints * 0.5f;

        float w = 0;
        foreach (char c in run.Text)
            w += GetAdvanceWidth(metrics, c);
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
        if (metrics.AdvanceWidths.TryGetValue(c, out var w))
            return w;
        return metrics.SizePoints * 0.5f;
    }

    private static FontStyle ConvertStyle(bool bold, bool italic)
    {
        if (bold && italic) return FontStyle.BoldItalic;
        if (bold) return FontStyle.Bold;
        if (italic) return FontStyle.Italic;
        return FontStyle.Regular;
    }
}
