namespace PdfEr.Core.Domain.Barcodes;

public enum BarcodeType
{
    Code128,
    Ean13,
    Ean8,
    UpcA,
    UpcE,
    Code39,
    QrCode,
    DataMatrix,
    Pdf417
}

public sealed class BarcodeDefinition
{
    public BarcodeType Type { get; set; }
    public string Value { get; set; } = string.Empty;
    public float HeightMm { get; set; } = 12f;
    public float NarrowBarWidthMm { get; set; } = 0.33f;
    public bool ShowText { get; set; } = true;
    public string? ForegroundColor { get; set; }
    public string? BackgroundColor { get; set; }
}

public sealed class BarcodeGlyph
{
    public required byte[] Pattern { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class BarcodeRenderResult
{
    public required string EncodedData { get; set; }
    public float WidthMm { get; set; }
    public float HeightMm { get; set; }
    public List<BarcodeBar> Bars { get; } = new();
}

public sealed class BarcodeBar
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public bool IsBar { get; set; }
}
