namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class DivHandler : BlockTagHandler
{
    public override string[] HandledTags => new[] { "div" };
}
