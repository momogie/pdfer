namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class AnchorHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "a" };
}
