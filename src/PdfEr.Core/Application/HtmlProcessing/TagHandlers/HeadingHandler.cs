namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class HeadingHandler : BlockTagHandler
{
    public override string[] HandledTags => new[] { "h1", "h2", "h3", "h4", "h5", "h6" };
}
