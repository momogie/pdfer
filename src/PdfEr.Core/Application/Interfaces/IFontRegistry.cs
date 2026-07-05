using PdfEr.Core.Domain.Typography;

namespace PdfEr.Core.Application.Interfaces;

public interface IFontRegistry
{
    FontMetrics? GetMetrics(string familyName, FontStyle style, float sizePoints);
    FontDefinition? FindFont(string familyName, FontStyle style);
    FontDefinition? FindFontWithFallback(string familyName, FontStyle style);
    IReadOnlyList<string> AvailableFamilies { get; }
    string? ResolveFilePath(string familyName, FontStyle style);
    void RegisterFont(string familyName, FontStyle style, byte[] fontData);
}
