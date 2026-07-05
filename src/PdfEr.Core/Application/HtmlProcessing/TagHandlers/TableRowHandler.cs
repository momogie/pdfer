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

        tableDef.Rows.Add(new TableRow());

        var dummy = new BlockBox { TagName = "tr" };
        context.CurrentBlock = dummy;
    }

    public void Close(TagContext context)
    {
    }
}
