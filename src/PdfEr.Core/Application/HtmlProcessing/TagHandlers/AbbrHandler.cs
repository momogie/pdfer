namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class AbbrHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "abbr", "acronym" };
}
