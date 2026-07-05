namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class SubScriptHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "sub" };
}
