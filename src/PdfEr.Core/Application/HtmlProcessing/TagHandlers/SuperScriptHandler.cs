namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class SuperScriptHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "sup" };
}
