using Microsoft.Extensions.Logging;
using PdfEr.Core.Application.Interfaces;
using SkiaSharp;
using Svg.Skia;

namespace PdfEr.Infrastructure.Services;

public sealed class SvgRasterizer : ISvgRasterizer
{
    private readonly ILogger<SvgRasterizer> _logger;

    public SvgRasterizer(ILogger<SvgRasterizer> logger)
    {
        _logger = logger;
    }

    public ImageLoadResult? Rasterize(string svgXml, int? widthHint, int? heightHint)
    {
        try
        {
            var svg = new SKSvg();
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgXml));
            svg.Load(stream);
            var picture = svg.Picture;
            if (picture == null)
            {
                _logger.LogWarning("SVG produced no picture");
                return null;
            }

            var bounds = picture.CullRect;
            int w = widthHint ?? (bounds.Width > 0 ? (int)bounds.Width : 100);
            int h = heightHint ?? (bounds.Height > 0 ? (int)bounds.Height : 100);
            if (w <= 0 || h <= 0)
            {
                _logger.LogWarning("SVG has invalid dimensions: {W}x{H}", w, h);
                return null;
            }

            using var bitmap = new SKBitmap(w, h);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawPicture(picture, 0, 0);

            var data = new byte[w * h * 3];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    int offset = (y * w + x) * 3;
                    data[offset] = pixel.Red;
                    data[offset + 1] = pixel.Green;
                    data[offset + 2] = pixel.Blue;
                }
            }

            return new ImageLoadResult
            {
                Data = data,
                PixelWidth = w,
                PixelHeight = h
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rasterize SVG");
            return null;
        }
    }
}
