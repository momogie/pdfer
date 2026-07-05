namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class UnderlineHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "u", "ins" };
}
