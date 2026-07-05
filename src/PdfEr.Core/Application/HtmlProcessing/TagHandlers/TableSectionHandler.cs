using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class TableSectionHandler : ITagHandler
{
    public string[] HandledTags => new[] { "thead", "tbody", "tfoot" };

    public void Open(TagContext context)
    {
        if (context.TableDef != null)
        {
            context.TableDef.InHeader = string.Equals(context.TagName, "thead", StringComparison.OrdinalIgnoreCase);
            context.TableDef.InFooter = string.Equals(context.TagName, "tfoot", StringComparison.OrdinalIgnoreCase);
        }

        var dummy = new BlockBox { TagName = context.TagName };
        context.CurrentBlock = dummy;
    }

    public void Close(TagContext context)
    {
        if (context.TableDef != null)
        {
            context.TableDef.InHeader = false;
            context.TableDef.InFooter = false;
        }
    }
}
