using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class HrHandler : ITagHandler
{
    public string[] HandledTags => new[] { "hr" };

    public void Open(TagContext context)
    {
        var box = context.LayoutEngine.CreateBlock("hr", context.Attributes, context.ParentStyle);
        box.Height = box.BorderTop + box.BorderBottom;
        box.TextContent = "";
        context.CurrentBlock = box;
    }

    public void Close(TagContext context)
    {
    }
}
