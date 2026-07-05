namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class BoldHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "b", "strong" };
}
