namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class ItalicHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "i", "em" };
}
