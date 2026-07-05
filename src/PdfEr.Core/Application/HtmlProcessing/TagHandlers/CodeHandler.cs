namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class CodeHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "code", "pre" };
}
