namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class CaptionHandler : BlockTagHandler
{
    public override string[] HandledTags => new[] { "caption" };
}
