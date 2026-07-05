using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.Tables;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class TableHandler : ITagHandler
{
    public string[] HandledTags => new[] { "table" };

    public void Open(TagContext context)
    {
        if (context.TableDef != null)
            context.TableDef.Current = new TableDefinition
            {
                Style = context.ComputedStyle
            };

        var box = context.LayoutEngine.CreateBlock(
            context.TagName, context.Attributes, context.ParentStyle);
        context.CurrentBlock = box;
    }

    public void Close(TagContext context)
    {
        var tableDef = context.TableDef?.Current;
        if (tableDef == null || tableDef.Rows.Count == 0)
        {
        if (context.TableDef != null) context.TableDef.Current = null;
            context.CurrentBlock = null;
            return;
        }

        var engine = new TableLayoutEngine();
        var layout = engine.ComputeLayout(tableDef, context.CurrentPage.ContentBox.Width);

        // Find header row indices for multi-page support
        var headerRowIndices = new List<int>();
        for (int i = 0; i < tableDef.Rows.Count; i++)
        {
            if (tableDef.Rows[i].IsHeader)
                headerRowIndices.Add(i);
        }

        float contentX = context.CurrentPage.ContentBox.X;
        float currentY = context.LayoutEngine.CurrentY;
        float pageBottom = context.CurrentPage.ContentBox.Bottom;

        foreach (var rowLayout in layout.Rows)
        {
            float rowHeight = rowLayout.Height;

            // Check if row fits on current page; if not, start new page
            bool isHeaderRow = headerRowIndices.Contains(rowLayout.RowIndex);
            if (!isHeaderRow && currentY + rowHeight > pageBottom)
            {
                context.LayoutEngine.AdvanceY(pageBottom - currentY);
                context.LayoutEngine.BreakPage(context.Config);
                context.CurrentPage = context.LayoutEngine.CurrentPage;

                // Re-fetch page info after page break
                contentX = context.CurrentPage.ContentBox.X;
                currentY = context.LayoutEngine.CurrentY;

                // Repeat header rows on new page
                foreach (var hdrIdx in headerRowIndices)
                {
                    if (hdrIdx >= layout.Rows.Count) continue;
                    var hdrLayout = layout.Rows[hdrIdx];
                    foreach (var cellLayout in hdrLayout.Cells)
                    {
                        if (string.IsNullOrWhiteSpace(cellLayout.TextContent))
                            continue;

                        var cellBox = CreateCellBox(contentX, currentY, cellLayout);
                        context.CurrentPage.Blocks.Add(cellBox);
                    }
                    currentY += hdrLayout.Height;
                }
            }

            // Add cell blocks for this row
            foreach (var cellLayout in rowLayout.Cells)
            {
                if (string.IsNullOrWhiteSpace(cellLayout.TextContent))
                    continue;

                var cellBox = CreateCellBox(contentX, currentY, cellLayout);
                context.CurrentPage.Blocks.Add(cellBox);
            }

            currentY += rowHeight;
        }

        float totalHeight = currentY - context.LayoutEngine.CurrentY;
        if (totalHeight > 0)
            context.LayoutEngine.AdvanceY(totalHeight);

        if (context.TableDef != null) context.TableDef.Current = null;
        context.CurrentBlock = null;
    }

    private static BlockBox CreateCellBox(float contentX, float baseY, TableCellLayout cellLayout)
    {
        var cellBox = new BlockBox
        {
            TagName = "td",
            TextContent = cellLayout.TextContent,
            X = contentX + cellLayout.X,
            Y = baseY + cellLayout.Y,
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

        return cellBox;
    }

    private static float GetFontSize(CssDeclarationBlock? style, float defaultSize)
    {
        var val = style?.GetPropertyValue("font-size");
        if (string.IsNullOrWhiteSpace(val)) return defaultSize;
        val = val.Trim().ToLowerInvariant();
        if (val.EndsWith("px") && float.TryParse(val[..^2], out var px)) return px * 0.2646f;
        if (val.EndsWith("pt") && float.TryParse(val[..^2], out var pt)) return pt * 0.3528f;
        if (val.EndsWith("mm") && float.TryParse(val[..^2], out var mm)) return mm;
        if (float.TryParse(val, out var n)) return n;
        return defaultSize;
    }
}
