using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Tables;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class TableRowHandler : ITagHandler
{
    public string[] HandledTags => new[] { "tr" };

    public void Open(TagContext context)
    {
        var tableDef = context.TableDef?.Current;
        if (tableDef == null) return;

        var row = new TableRow();
        if (context.TableDef != null)
        {
            row.IsHeader = context.TableDef.InHeader;
            row.IsFooter = context.TableDef.InFooter;
        }
        tableDef.Rows.Add(row);

        var dummy = new BlockBox { TagName = "tr" };
        context.CurrentBlock = dummy;
    }

    public void Close(TagContext context)
    {
    }
}
