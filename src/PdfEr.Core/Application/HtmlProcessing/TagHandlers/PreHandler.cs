namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class PreHandler : BlockTagHandler
{
    public override string[] HandledTags => new[] { "pre" };
}
