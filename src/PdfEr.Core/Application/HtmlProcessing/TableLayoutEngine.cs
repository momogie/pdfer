using PdfEr.Core.Domain.Tables;

namespace PdfEr.Core.Application.HtmlProcessing;

public sealed class TableLayoutEngine
{
    public TableLayoutResult ComputeLayout(TableDefinition table, float availableWidth)
    {
        var result = new TableLayoutResult
        {
            TotalWidth = availableWidth
        };

        ComputeColumnWidths(table, result, availableWidth);
        LayoutRows(table, result);
        return result;
    }

    private void ComputeColumnWidths(TableDefinition table, TableLayoutResult result, float availableWidth)
    {
        int colCount = table.ColumnCount;
        var colWidths = new float[colCount];
        var minWidths = new float[colCount];
        var maxWidths = new float[colCount];
        var fixedCols = new bool[colCount];

        for (int i = 0; i < colCount; i++)
        {
            colWidths[i] = availableWidth / colCount;
            minWidths[i] = 10f;
            maxWidths[i] = availableWidth;
        }

        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                int col = cell.ColumnIndex;
                int spanEnd = col + cell.ColSpan;
                CellDimension dims = EstimateCellDimensions(cell);
                float minPerCol = dims.MinWidth / cell.ColSpan;
                float maxPerCol = dims.MaxWidth / cell.ColSpan;

                for (int c = col; c < spanEnd; c++)
                {
                    if (c < colCount)
                    {
                        minWidths[c] = Math.Max(minWidths[c], minPerCol);
                        maxWidths[c] = Math.Min(Math.Max(maxWidths[c], maxPerCol), availableWidth);
                    }
                }

                if (cell.Style?.TryGetProperty("width", out var wVal) == true && float.TryParse(wVal.RawValue?.Replace("px", "").Replace("pt", ""), out float w) && w > 0)
                {
                    float perColWidth = w / cell.ColSpan;
                    for (int c = col; c < spanEnd && c < colCount; c++)
                    {
                        colWidths[c] = Math.Max(colWidths[c], perColWidth);
                        fixedCols[c] = true;
                    }
                }
            }
        }

        float totalMin = minWidths.Sum();
        float remaining = availableWidth;

        if (totalMin >= availableWidth)
        {
            for (int i = 0; i < colCount; i++)
                colWidths[i] = minWidths[i];
        }
        else
        {
            float fixedTotal = 0;
            int autoCount = 0;
            for (int i = 0; i < colCount; i++)
            {
                if (fixedCols[i])
                {
                    colWidths[i] = Math.Max(colWidths[i], minWidths[i]);
                    fixedTotal += colWidths[i];
                }
                else autoCount++;
            }

            remaining = availableWidth - fixedTotal;
            if (autoCount > 0)
            {
                float autoWidth = Math.Max(0, remaining / autoCount);
                for (int i = 0; i < colCount; i++)
                {
                    if (!fixedCols[i])
                        colWidths[i] = Math.Max(minWidths[i], autoWidth);
                }
            }
        }

        for (int i = 0; i < colCount; i++)
        {
            result.Columns.Add(new TableColumnInfo
            {
                Index = i,
                Width = colWidths[i],
                MinWidth = minWidths[i],
                MaxWidth = maxWidths[i]
            });
        }

        result.TotalWidth = result.Columns.Sum(c => c.Width);
    }

    private void LayoutRows(TableDefinition table, TableLayoutResult result)
    {
        float y = 0;

        foreach (var row in table.Rows)
        {
            var rowLayout = new TableRowLayout
            {
                RowIndex = result.Rows.Count,
                Y = y,
                Height = row.Height > 0 ? row.Height : 20f
            };

            foreach (var cell in row.Cells)
            {
                float cellX = 0;
                for (int c = 0; c < cell.ColumnIndex && c < result.Columns.Count; c++)
                    cellX += result.Columns[c].Width;

                float cellWidth = 0;
                for (int c = cell.ColumnIndex; c < cell.ColumnIndex + cell.ColSpan && c < result.Columns.Count; c++)
                    cellWidth += result.Columns[c].Width;

                rowLayout.Cells.Add(new TableCellLayout
                {
                    RowIndex = cell.RowIndex,
                    ColumnIndex = cell.ColumnIndex,
                    ColSpan = cell.ColSpan,
                    RowSpan = cell.RowSpan,
                    X = cellX,
                    Y = y,
                    Width = cellWidth,
                    Height = rowLayout.Height,
                    TextContent = cell.TextContent,
                    Style = cell.ComputedStyle ?? cell.Style
                });
            }

            result.Rows.Add(rowLayout);
            y += rowLayout.Height;
        }

        result.TotalHeight = y;
    }

    private CellDimension EstimateCellDimensions(TableCell cell)
    {
        int textLen = cell.TextContent?.Length ?? 0;
        return new CellDimension
        {
            MinWidth = Math.Max(10f, textLen * 2f / (cell.ColSpan > 1 ? cell.ColSpan : 1)),
            MaxWidth = Math.Max(10f, textLen * 8f)
        };
    }

    private struct CellDimension
    {
        public float MinWidth;
        public float MaxWidth;
    }
}
