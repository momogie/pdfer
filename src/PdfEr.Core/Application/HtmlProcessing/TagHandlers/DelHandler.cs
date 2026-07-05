namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class DelHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "del" };
}
