namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class BlockquoteHandler : BlockTagHandler
{
    public override string[] HandledTags => new[] { "blockquote" };
}
