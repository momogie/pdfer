using Microsoft.Extensions.Logging;
using PdfEr.Core.Application.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PdfEr.Infrastructure.Services;

public sealed class ImageService : IImageService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ImageService> _logger;
    private bool _disposed;

    public ImageService(ILogger<ImageService> logger) : this(logger, new HttpClient())
    {
    }

    public ImageService(ILogger<ImageService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public ImageLoadResult? LoadImage(string source, string? basePath)
    {
        try
        {
            byte[] imageBytes;

            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                imageBytes = _httpClient.GetByteArrayAsync(source)
                    .GetAwaiter().GetResult();
            }
            else
            {
                var resolvedPath = Path.IsPathRooted(source)
                    ? source
                    : !string.IsNullOrEmpty(basePath)
                        ? Path.Combine(basePath, source)
                        : source;

                if (!File.Exists(resolvedPath))
                {
                    _logger.LogWarning("Image file not found: {Path}", resolvedPath);
                    return null;
                }

                imageBytes = File.ReadAllBytes(resolvedPath);
            }

            using var image = Image.Load<Rgba32>(imageBytes);
            int width = image.Width;
            int height = image.Height;

            var rgbData = new byte[width * height * 3];
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        int offset = (y * width + x) * 3;
                        rgbData[offset] = row[x].R;
                        rgbData[offset + 1] = row[x].G;
                        rgbData[offset + 2] = row[x].B;
                    }
                }
            });

            return new ImageLoadResult
            {
                Data = rgbData,
                PixelWidth = width,
                PixelHeight = height
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load image: {Source}", source);
            return null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }
}
