namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class ParagraphHandler : BlockTagHandler
{
    public override string[] HandledTags => new[] { "p" };
}
