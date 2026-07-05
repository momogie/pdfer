using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class ImgHandler : ITagHandler
{
    private readonly IImageService _imageService;

    public ImgHandler(IImageService imageService)
    {
        _imageService = imageService;
    }

    public string[] HandledTags => new[] { "img" };

    public void Open(TagContext context)
    {
        var src = context.Attributes.GetValueOrDefault("src", "");
        if (string.IsNullOrWhiteSpace(src))
            return;

        var attrWidth = TryParsePixel(context.Attributes.GetValueOrDefault("width"));
        var attrHeight = TryParsePixel(context.Attributes.GetValueOrDefault("height"));

        var basePath = context.Config.ImageBasePath;
        var result = _imageService.LoadImage(src, basePath);
        if (result == null)
            return;

        int imgWidth = attrWidth ?? result.PixelWidth;
        int imgHeight = attrHeight ?? result.PixelHeight;
        if (imgWidth <= 0 || imgHeight <= 0)
            return;

        float pxToMm = 25.4f / 96f;
        float widthMm = imgWidth * pxToMm;
        float heightMm = imgHeight * pxToMm;

        var box = context.LayoutEngine.CreateBlock("img", context.Attributes, context.ParentStyle);
        box.Width = widthMm;
        box.Height = heightMm;
        box.TextContent = "";

        var inline = new InlineBox
        {
            Type = InlineBoxType.Image,
            Width = widthMm,
            Height = heightMm,
            ImageSource = src,
            ImageData = result.Data,
            ImagePixelWidth = imgWidth,
            ImagePixelHeight = imgHeight,
            X = box.X,
            Y = box.Y
        };
        box.InlineContent.Add(inline);

        context.CurrentBlock = box;
    }

    public void Close(TagContext context)
    {
    }

    private static int? TryParsePixel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Trim().ToLowerInvariant();
        if (clean.EndsWith("px")) clean = clean[..^2].Trim();
        if (int.TryParse(clean, out var result)) return result;
        return null;
    }
}
