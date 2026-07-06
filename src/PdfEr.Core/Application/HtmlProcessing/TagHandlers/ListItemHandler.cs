using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class ListItemHandler : ITagHandler
{
    public string[] HandledTags => new[] { "li" };

    public void Open(TagContext context)
    {
        var box = context.LayoutEngine.CreateBlock("li", context.Attributes, context.ParentStyle, context.Element);

        var listState = context.ListStack.Count > 0 ? context.ListStack.Peek() : null;
        var marker = listState?.GetMarker() ?? "\u2022";

        var indent = 15f;
        box.MarginLeft = indent;
        box.X += indent;
        box.Width -= indent;

        box.TextContent = marker;

        if (listState != null && listState.IsOrdered)
            listState.Counter++;

        context.CurrentBlock = box;
    }

    public void Close(TagContext context)
    {
    }
}
