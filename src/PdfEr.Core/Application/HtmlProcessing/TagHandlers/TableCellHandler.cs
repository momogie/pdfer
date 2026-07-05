using System.Text;
using AngleSharp.Dom;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Tables;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class TableCellHandler : ITagHandler
{
    public string[] HandledTags => new[] { "td", "th" };

    public void Open(TagContext context)
    {
        var element = context.Element;
        if (element == null) return;

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

        // Separate nested tables from other content
        var textContent = new StringBuilder();
        var nestedTables = new List<IElement>();

        foreach (var child in element.ChildNodes)
        {
            if (child is IText textNode)
            {
                var text = textNode.TextContent?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (textContent.Length > 0) textContent.Append(' ');
                    textContent.Append(text);
                }
            }
            else if (child is IElement childEl)
            {
                var tag = childEl.TagName.ToLowerInvariant();
                if (tag == "table")
                {
                    nestedTables.Add(childEl);
                }
                else
                {
                    var childText = CollectTextRecursive(childEl);
                    if (!string.IsNullOrWhiteSpace(childText))
                    {
                        if (textContent.Length > 0) textContent.Append(' ');
                        textContent.Append(childText);
                    }
                }
            }
        }

        var cell = new TableCell
        {
            ColumnIndex = row.Cells.Count,
            TextContent = textContent.ToString(),
            ColSpan = colSpan,
            RowSpan = rowSpan,
            IsHeader = string.Equals(context.TagName, "th", StringComparison.OrdinalIgnoreCase),
            ComputedStyle = context.ComputedStyle,
        };

        row.Cells.Add(cell);

        if (tableDef.ColumnCount < row.Cells.Count)
            tableDef.ColumnCount = row.Cells.Count;

        // Process nested tables
        foreach (var nestedTableEl in nestedTables)
        {
            ProcessNestedTable(nestedTableEl, context);
        }
    }

    public void Close(TagContext context)
    {
    }

    private static void ProcessNestedTable(IElement tableEl, TagContext context)
    {
        var nestedDef = BuildTableDefinition(tableEl);
        if (nestedDef == null || nestedDef.Rows.Count == 0) return;

        var engine = new TableLayoutEngine();
        var layout = engine.ComputeLayout(nestedDef, context.CurrentPage.ContentBox.Width);

        float contentX = context.CurrentPage.ContentBox.X;
        float startY = context.LayoutEngine.CurrentY;

        foreach (var rowLayout in layout.Rows)
        {
            foreach (var cellLayout in rowLayout.Cells)
            {
                if (string.IsNullOrWhiteSpace(cellLayout.TextContent))
                    continue;

                var cellBox = new BlockBox
                {
                    TagName = "td",
                    TextContent = cellLayout.TextContent,
                    X = contentX + cellLayout.X,
                    Y = startY + cellLayout.Y,
                    Width = cellLayout.Width,
                    Height = cellLayout.Height,
                    ComputedStyle = cellLayout.Style,
                };

                if (cellLayout.Style != null)
                    LayoutEngine.ApplyBoxModel(cellBox, cellLayout.Style);

                cellBox.InlineContent.Add(new InlineBox
                {
                    Text = cellLayout.TextContent,
                    Type = InlineBoxType.Text,
                    X = cellBox.X + cellBox.PaddingLeft,
                    Y = cellBox.Y + cellBox.PaddingTop,
                    Width = cellLayout.Width,
                    Height = cellLayout.Height,
                    ComputedStyle = cellBox.ComputedStyle,
                });

                context.CurrentPage.Blocks.Add(cellBox);
            }
        }

        float totalHeight = layout.TotalHeight > 0 ? layout.TotalHeight : 20f;
        context.LayoutEngine.AdvanceY(totalHeight);
    }

    private static TableDefinition? BuildTableDefinition(IElement tableEl)
    {
        var def = new TableDefinition { ColumnCount = 0 };
        var rows = tableEl.QuerySelectorAll("tr");
        if (rows.Length == 0) return null;

        foreach (var rowEl in rows)
        {
            var row = new TableRow();
            var cells = rowEl.QuerySelectorAll("td, th");
            foreach (var cellEl in cells)
            {
                var text = CollectTextRecursive(cellEl);
                var colSpan = 1;
                var csAttr = cellEl.GetAttribute("colspan");
                if (!string.IsNullOrWhiteSpace(csAttr) && int.TryParse(csAttr, out var cs))
                    colSpan = cs;

                var cell = new TableCell
                {
                    ColumnIndex = row.Cells.Count,
                    TextContent = text,
                    ColSpan = colSpan,
                    IsHeader = cellEl.TagName.Equals("th", StringComparison.OrdinalIgnoreCase),
                };
                row.Cells.Add(cell);
            }
            if (row.Cells.Count > def.ColumnCount)
                def.ColumnCount = row.Cells.Count;
            def.Rows.Add(row);
        }
        return def;
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
