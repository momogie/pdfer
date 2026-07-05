using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Application.Interfaces;

public interface ITagHandler
{
    string[] HandledTags { get; }
    void Open(TagContext context);
    void Close(TagContext context);
}
