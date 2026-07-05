using PdfEr.Core.Domain.ValueObjects;

namespace PdfEr.Core.Application.Interfaces;

public interface IPdfConverter
{
    byte[] ConvertHtmlToPdf(string html, PdfConverterConfiguration? config = null);
    Task<byte[]> ConvertHtmlToPdfAsync(string html, CancellationToken cancellationToken = default);
    void ConvertHtmlToPdfToFile(string html, string filePath, PdfConverterConfiguration? config = null);
    Task ConvertHtmlToPdfToFileAsync(string html, string filePath, PdfConverterConfiguration? config = null, CancellationToken cancellationToken = default);
}
