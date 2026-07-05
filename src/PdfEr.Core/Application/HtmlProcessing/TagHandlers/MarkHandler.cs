namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class MarkHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "mark" };
}
