using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class BreakHandler : ITagHandler
{
    public string[] HandledTags => new[] { "br" };

    public void Open(TagContext context)
    {
        var box = context.LayoutEngine.CreateBlock("br", context.Attributes, context.ParentStyle);
        box.TextContent = "\n";
        context.CurrentBlock = box;
    }

    public void Close(TagContext context)
    {
    }
}
