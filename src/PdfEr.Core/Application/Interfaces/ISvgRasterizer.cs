namespace PdfEr.Core.Application.Interfaces;

public interface ISvgRasterizer
{
    ImageLoadResult? Rasterize(string svgXml, int? widthHint, int? heightHint);
}
