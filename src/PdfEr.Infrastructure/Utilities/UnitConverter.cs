using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Enums;

namespace PdfEr.Infrastructure.Utilities;

public sealed class UnitConverter : IUnitConverter
{
    private const float PointsPerInch = 72f;
    private const float MillimetersPerInch = 25.4f;
    private const float PicasPerInch = 6f;

    public float ConvertToMillimeters(float value, UnitOfMeasure fromUnit)
    {
        return fromUnit switch
        {
            UnitOfMeasure.Millimeter => value,
            UnitOfMeasure.Centimeter => value * 10f,
            UnitOfMeasure.Inch => value * MillimetersPerInch,
            UnitOfMeasure.Point => value * MillimetersPerInch / PointsPerInch,
            UnitOfMeasure.Pica => value * MillimetersPerInch / PicasPerInch,
            _ => throw new ArgumentOutOfRangeException(nameof(fromUnit), $"Cannot convert {fromUnit} to millimeters without context")
        };
    }

    public float ConvertToPoints(float value, UnitOfMeasure fromUnit)
    {
        return fromUnit switch
        {
            UnitOfMeasure.Point => value,
            UnitOfMeasure.Millimeter => value * PointsPerInch / MillimetersPerInch,
            UnitOfMeasure.Centimeter => value * PointsPerInch / MillimetersPerInch * 10f,
            UnitOfMeasure.Inch => value * PointsPerInch,
            UnitOfMeasure.Pica => value * PointsPerInch / PicasPerInch,
            UnitOfMeasure.Pixel => ConvertPixelsToPoints((int)value, 96),
            _ => throw new ArgumentOutOfRangeException(nameof(fromUnit), $"Cannot convert {fromUnit} to points without context")
        };
    }

    public float ConvertPixelsToPoints(int pixels, int dpi)
    {
        return pixels * PointsPerInch / dpi;
    }

    public float ConvertMillimetersToPoints(float mm)
    {
        return mm * PointsPerInch / MillimetersPerInch;
    }

    public float ConvertPointsToMillimeters(float pt)
    {
        return pt * MillimetersPerInch / PointsPerInch;
    }
}
