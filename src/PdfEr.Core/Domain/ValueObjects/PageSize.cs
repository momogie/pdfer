using PdfEr.Core.Domain.Enums;

namespace PdfEr.Core.Domain.ValueObjects;

public readonly struct PageSize
{
    public float WidthMillimeters { get; }
    public float HeightMillimeters { get; }

    public PageSize(float widthMm, float heightMm)
    {
        if (widthMm <= 0) throw new ArgumentOutOfRangeException(nameof(widthMm), "Width must be positive");
        if (heightMm <= 0) throw new ArgumentOutOfRangeException(nameof(heightMm), "Height must be positive");
        WidthMillimeters = widthMm;
        HeightMillimeters = heightMm;
    }

    public PageSize Rotate() => new(HeightMillimeters, WidthMillimeters);

    public static PageSize FromFormat(PageFormat format)
    {
        var (w, h) = format switch
        {
            PageFormat.A0 => (841f, 1189f),
            PageFormat.A1 => (594f, 841f),
            PageFormat.A2 => (420f, 594f),
            PageFormat.A3 => (297f, 420f),
            PageFormat.A4 => (210f, 297f),
            PageFormat.A5 => (148f, 210f),
            PageFormat.A6 => (105f, 148f),
            PageFormat.B0 => (1000f, 1414f),
            PageFormat.B1 => (707f, 1000f),
            PageFormat.B2 => (500f, 707f),
            PageFormat.B3 => (353f, 500f),
            PageFormat.B4 => (250f, 353f),
            PageFormat.B5 => (176f, 250f),
            PageFormat.B6 => (125f, 176f),
            PageFormat.Letter => (215.9f, 279.4f),
            PageFormat.Legal => (215.9f, 355.6f),
            PageFormat.Ledger => (279.4f, 431.8f),
            PageFormat.Tabloid => (431.8f, 279.4f),
            PageFormat.Executive => (184.15f, 266.7f),
            PageFormat.Folio => (210f, 330f),
            PageFormat.Demy => (445f, 572f),
            PageFormat.Royal => (508f, 635f),
            _ => throw new ArgumentOutOfRangeException(nameof(format), $"Unknown page format: {format}")
        };
        return new PageSize(w, h);
    }

    public static PageSize FromOrientation(PageSize size, PageOrientation orientation)
    {
        if (orientation == PageOrientation.Portrait)
            return new PageSize(Math.Min(size.WidthMillimeters, size.HeightMillimeters), Math.Max(size.WidthMillimeters, size.HeightMillimeters));
        return new PageSize(Math.Max(size.WidthMillimeters, size.HeightMillimeters), Math.Min(size.WidthMillimeters, size.HeightMillimeters));
    }

    public float WidthPoints => WidthMillimeters * 72f / 25.4f;
    public float HeightPoints => HeightMillimeters * 72f / 25.4f;

    public override string ToString() => $"{WidthMillimeters:F1}x{HeightMillimeters:F1}mm";
}
