namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class SpanHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "span" };
}
