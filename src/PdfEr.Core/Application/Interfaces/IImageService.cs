namespace PdfEr.Core.Application.Interfaces;

public sealed class ImageLoadResult
{
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
}

public interface IImageService
{
    ImageLoadResult? LoadImage(string source, string? basePath);
}
