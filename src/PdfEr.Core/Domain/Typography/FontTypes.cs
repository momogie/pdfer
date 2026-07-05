using PdfEr.Core.Domain.ValueObjects;

namespace PdfEr.Core.Domain.Typography;

public enum FontStyle
{
    Regular,
    Italic,
    Bold,
    BoldItalic
}

public sealed class FontDefinition
{
    public required string FamilyName { get; set; }
    public FontStyle Style { get; set; } = FontStyle.Regular;
    public float SizePoints { get; set; } = 12f;
    public string? FilePath { get; set; }
    public byte[]? FontData { get; set; }
    public bool IsEmbedded { get; set; }
}

public sealed class FontMetrics
{
    public required string FamilyName { get; set; }
    public FontStyle Style { get; set; }
    public float SizePoints { get; set; }
    public float Ascender { get; set; }
    public float Descender { get; set; }
    public float LineHeight { get; set; }
    public float CapHeight { get; set; }
    public float XHeight { get; set; }
    public float UnderlinePosition { get; set; }
    public float UnderlineThickness { get; set; }
    public float StrikeoutPosition { get; set; }
    public float StrikeoutThickness { get; set; }
    public Dictionary<char, float> AdvanceWidths { get; set; } = new();
    public Dictionary<(char, char), float> KerningPairs { get; set; } = new();
}

public sealed class GlyphPosition
{
    public char Character { get; set; }
    public float AdvanceWidth { get; set; }
    public float BearingX { get; set; }
    public float BearingY { get; set; }
    public float Height { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int GlyphIndex { get; set; }
}

public sealed class TextRun
{
    public string Text { get; set; } = string.Empty;
    public FontDefinition Font { get; set; } = new() { FamilyName = "Helvetica" };
    public float SizePoints { get; set; } = 12f;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strikethrough { get; set; }
    public DocumentColor? Color { get; set; }
    public DocumentColor? BackgroundColor { get; set; }
    public string? LinkUrl { get; set; }
}

public sealed class TextLine
{
    public List<TextRun> Runs { get; } = new();
    public float Width { get; set; }
    public float Height { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;
    public float LineSpacing { get; set; } = 1.15f;
}

public enum TextAlignment
{
    Left,
    Center,
    Right,
    Justify
}

public sealed class TextLayoutResult
{
    public List<TextLine> Lines { get; } = new();
    public float TotalWidth { get; set; }
    public float TotalHeight { get; set; }
    public int CharacterCount { get; set; }
    public bool Overflow { get; set; }
}
