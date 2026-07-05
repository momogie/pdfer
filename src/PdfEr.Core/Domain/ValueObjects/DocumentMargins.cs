namespace PdfEr.Core.Domain.ValueObjects;

public readonly struct DocumentMargins
{
    public float Top { get; }
    public float Bottom { get; }
    public float Left { get; }
    public float Right { get; }
    public float Header { get; }
    public float Footer { get; }

    public DocumentMargins(float all, float headerFooter = 5f)
        : this(all, all, all, all, headerFooter, headerFooter)
    {
    }

    public DocumentMargins(float top, float bottom, float left, float right, float header = 5f, float footer = 5f)
    {
        if (top < 0) throw new ArgumentOutOfRangeException(nameof(top));
        if (bottom < 0) throw new ArgumentOutOfRangeException(nameof(bottom));
        if (left < 0) throw new ArgumentOutOfRangeException(nameof(left));
        if (right < 0) throw new ArgumentOutOfRangeException(nameof(right));
        if (header < 0) throw new ArgumentOutOfRangeException(nameof(header));
        if (footer < 0) throw new ArgumentOutOfRangeException(nameof(footer));

        Top = top;
        Bottom = bottom;
        Left = left;
        Right = right;
        Header = header;
        Footer = footer;
    }

    public static DocumentMargins Default => new(15f);

    public override string ToString() => $"T:{Top} B:{Bottom} L:{Left} R:{Right} H:{Header} F:{Footer}mm";
}
