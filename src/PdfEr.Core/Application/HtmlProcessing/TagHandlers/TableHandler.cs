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

                cellBox.InlineContent.Add(new InlineBox
                {
                    Text = cellLayout.TextContent,
                    Type = InlineBoxType.Text,
                    X = cellBox.X,
                    Y = cellBox.Y,
                    Width = cellLayout.Width,
                    Height = cellLayout.Height,
                    ComputedStyle = cellBox.ComputedStyle,
                });

                context.CurrentPage.Blocks.Add(cellBox);
            }
        }

        float totalHeight = layout.TotalHeight > 0 ? layout.TotalHeight : 20f;
        context.LayoutEngine.AdvanceY(totalHeight);

        if (context.TableDef != null) context.TableDef.Current = null;
        context.CurrentBlock = null;
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
