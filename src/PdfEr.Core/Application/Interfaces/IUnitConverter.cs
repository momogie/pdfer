using PdfEr.Core.Domain.Enums;

namespace PdfEr.Core.Application.Interfaces;

public interface IUnitConverter
{
    float ConvertToMillimeters(float value, UnitOfMeasure fromUnit);
    float ConvertToPoints(float value, UnitOfMeasure fromUnit);
    float ConvertPixelsToPoints(int pixels, int dpi);
    float ConvertMillimetersToPoints(float mm);
    float ConvertPointsToMillimeters(float pt);
}
