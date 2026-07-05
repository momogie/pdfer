using PdfEr.Core.Application.Interfaces;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public abstract class InlineTagHandler : ITagHandler
{
    public abstract string[] HandledTags { get; }

    public virtual void Open(TagContext context)
    {
    }

    public virtual void Close(TagContext context)
    {
    }
}
