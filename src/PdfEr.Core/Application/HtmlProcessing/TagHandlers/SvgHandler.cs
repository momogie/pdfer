using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class SvgHandler : ITagHandler
{
    private readonly ISvgRasterizer _svgRasterizer;

    public SvgHandler(ISvgRasterizer svgRasterizer)
    {
        _svgRasterizer = svgRasterizer;
    }

    public string[] HandledTags => new[] { "svg" };

    public void Open(TagContext context)
    {
        var svgXml = context.Element.OuterHtml;
        if (string.IsNullOrWhiteSpace(svgXml))
            return;

        var attrWidth = TryParseLength(context.Attributes.GetValueOrDefault("width"));
        var attrHeight = TryParseLength(context.Attributes.GetValueOrDefault("height"));

        var result = _svgRasterizer.Rasterize(svgXml, attrWidth, attrHeight);
        if (result == null)
            return;

        float pxToMm = 25.4f / 96f;
        float widthMm = result.PixelWidth * pxToMm;
        float heightMm = result.PixelHeight * pxToMm;

        var box = context.LayoutEngine.CreateBlock("svg", context.Attributes, context.ParentStyle);
        box.Width = widthMm;
        box.Height = heightMm;
        box.TextContent = "";

        var inline = new InlineBox
        {
            Type = InlineBoxType.Image,
            Width = widthMm,
            Height = heightMm,
            ImageSource = "",
            ImageData = result.Data,
            ImagePixelWidth = result.PixelWidth,
            ImagePixelHeight = result.PixelHeight,
            X = box.X,
            Y = box.Y
        };
        box.InlineContent.Add(inline);

        context.CurrentBlock = box;
    }

    public void Close(TagContext context)
    {
    }

    private static int? TryParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Trim().ToLowerInvariant();
        if (clean.EndsWith("px")) clean = clean[..^2].Trim();
        if (clean.EndsWith("pt")) clean = clean[..^2].Trim();
        if (int.TryParse(clean, out var result)) return result;
        return null;
    }
}
