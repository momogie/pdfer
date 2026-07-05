using System.Text;
using AngleSharp.Dom;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Tables;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class TableCellHandler : ITagHandler
{
    public string[] HandledTags => new[] { "td", "th" };

    public void Open(TagContext context)
    {
        var text = CollectTextRecursive(context.Element);

        var tableDef = context.TableDef?.Current;
        if (tableDef == null) return;

        var row = tableDef.Rows.LastOrDefault();
        if (row == null) return;

        var colSpan = 1;
        if (context.Attributes.TryGetValue("colspan", out var cs) && int.TryParse(cs, out var csVal))
            colSpan = csVal;

        var rowSpan = 1;
        if (context.Attributes.TryGetValue("rowspan", out var rs) && int.TryParse(rs, out var rsVal))
            rowSpan = rsVal;

        var cell = new TableCell
        {
            ColumnIndex = row.Cells.Count,
            TextContent = text,
            ColSpan = colSpan,
            RowSpan = rowSpan,
            IsHeader = string.Equals(context.TagName, "th", StringComparison.OrdinalIgnoreCase),
            ComputedStyle = context.ComputedStyle,
        };

        row.Cells.Add(cell);

        if (tableDef.ColumnCount < row.Cells.Count)
            tableDef.ColumnCount = row.Cells.Count;
    }

    public void Close(TagContext context)
    {
    }

    private static string CollectTextRecursive(IElement element)
    {
        var sb = new StringBuilder();
        foreach (var child in element.ChildNodes)
        {
            if (child is IText textNode)
            {
                var text = textNode.TextContent?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(text);
                }
            }
            else if (child is IElement childEl)
            {
                var childText = CollectTextRecursive(childEl);
                if (!string.IsNullOrWhiteSpace(childText))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(childText);
                }
            }
        }
        return sb.ToString();
    }
}
