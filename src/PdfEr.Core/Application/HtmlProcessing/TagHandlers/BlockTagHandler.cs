using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public abstract class BlockTagHandler : ITagHandler
{
    public abstract string[] HandledTags { get; }

    public virtual void Open(TagContext context)
    {
        var box = context.LayoutEngine.CreateBlock(
            context.TagName,
            context.Attributes,
            context.ParentStyle,
            context.Element);

        context.CurrentBlock = box;
    }

    public virtual void Close(TagContext context)
    {
    }
}
