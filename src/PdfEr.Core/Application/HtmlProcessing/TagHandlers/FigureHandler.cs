using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class FigureHandler : BlockTagHandler
{
    public override string[] HandledTags => new[] { "figure", "figcaption", "details", "summary" };
}
