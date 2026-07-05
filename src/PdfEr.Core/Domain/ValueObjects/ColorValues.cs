namespace PdfEr.Core.Domain.ValueObjects;

public interface DocumentColor { }

public readonly record struct RgbColor(byte R, byte G, byte B, byte A = 255) : DocumentColor
{
    public static readonly RgbColor Black = new(0, 0, 0);
    public static readonly RgbColor White = new(255, 255, 255);
    public static readonly RgbColor Red = new(255, 0, 0);
    public static readonly RgbColor Transparent = new(0, 0, 0, 0);

    public bool IsTransparent => A == 0;
}

public readonly record struct CmykColor(float C, float M, float Y, float K, float A = 1f) : DocumentColor
{
    public static readonly CmykColor Black = new(0, 0, 0, 1);
    public static readonly CmykColor White = new(0, 0, 0, 0);

    public bool IsTransparent => A <= 0f;
}

public readonly record struct GrayscaleColor(byte Gray, byte A = 255) : DocumentColor
{
    public static readonly GrayscaleColor Black = new(0);
    public static readonly GrayscaleColor White = new(255);
}

public readonly record struct SpotColor(string Name, CmykColor Alternative) : DocumentColor;
