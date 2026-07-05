namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class QuoteHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "q", "cite" };
}
