namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class SmallHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "small" };
}
