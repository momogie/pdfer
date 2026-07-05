using PdfEr.Core.Domain.Typography;

namespace PdfEr.Core.Application.Interfaces;

public interface ITextLayoutEngine
{
    TextLayoutResult LayoutText(string text, FontDefinition font, float maxWidth,
        TextAlignment alignment = TextAlignment.Left, float lineSpacing = 1.15f);

    TextLayoutResult LayoutRichText(IReadOnlyList<TextRun> runs, float maxWidth,
        TextAlignment alignment = TextAlignment.Left, float lineSpacing = 1.15f);

    float MeasureTextWidth(string text, FontDefinition font);
    int GetCharacterIndexAtPosition(string text, FontDefinition font, float xPosition);
}
