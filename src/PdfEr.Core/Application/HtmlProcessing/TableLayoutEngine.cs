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
        int colCount = Math.Max(table.ColumnCount, table.FixedColWidths.Count);
        var colWidths = new float[colCount];
        var minWidths = new float[colCount];
        var maxWidths = new float[colCount];
        var fixedCols = new bool[colCount];

        bool isFixedLayout = table.IsFixedLayout;

        // Apply fixed column widths from <colgroup>/<col>
        for (int i = 0; i < table.FixedColWidths.Count && i < colCount; i++)
        {
            if (table.FixedColWidths[i] > 0)
            {
                colWidths[i] = table.FixedColWidths[i];
                fixedCols[i] = true;
            }
        }

        for (int i = 0; i < colCount; i++)
        {
            if (colWidths[i] <= 0)
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
                        if (!isFixedLayout)
                            maxWidths[c] = Math.Min(Math.Max(maxWidths[c], maxPerCol), availableWidth);
                    }
                }

                // Cell-level width attribute
                var cellWidthVal = cell.Style?.GetPropertyValue("width")
                    ?? cell.ComputedStyle?.GetPropertyValue("width");
                if (!string.IsNullOrWhiteSpace(cellWidthVal) && cellWidthVal != "auto")
                {
                    float perColWidth = 0;
                    var wv = cellWidthVal.Trim().ToLowerInvariant();
                    if (wv.EndsWith("px") && float.TryParse(wv[..^2], out var wPx))
                        perColWidth = wPx * 0.2646f / cell.ColSpan;
                    else if (wv.EndsWith("pt") && float.TryParse(wv[..^2], out var wPt))
                        perColWidth = wPt * 0.3528f / cell.ColSpan;
                    else if (wv.EndsWith("mm") && float.TryParse(wv[..^2], out var wMm))
                        perColWidth = wMm / cell.ColSpan;
                    else if (float.TryParse(wv, out var wRaw) && wRaw > 0)
                        perColWidth = wRaw / cell.ColSpan;

                    if (perColWidth > 0)
                    {
                        for (int c = col; c < spanEnd && c < colCount; c++)
                        {
                            if (!fixedCols[c] || perColWidth > colWidths[c])
                                colWidths[c] = perColWidth;
                            fixedCols[c] = true;
                        }
                    }
                }
            }
        }

        if (isFixedLayout)
        {
            // In fixed layout, distribute remaining space proportionally
            float fixedTotal = 0;
            int autoCount = 0;
            for (int i = 0; i < colCount; i++)
            {
                if (fixedCols[i])
                    fixedTotal += colWidths[i];
                else
                    autoCount++;
            }

            float remaining = availableWidth - fixedTotal;
            if (autoCount > 0 && remaining > 0)
            {
                float autoWidth = remaining / autoCount;
                for (int i = 0; i < colCount; i++)
                    if (!fixedCols[i])
                        colWidths[i] = autoWidth;
            }
            else if (autoCount > 0 && remaining <= 0)
            {
                for (int i = 0; i < colCount; i++)
                    if (!fixedCols[i])
                        colWidths[i] = minWidths[i];
            }
        }
        else
        {
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
        }

        // Ensure no column ends up with zero or negative width
        float minColWidth = 2f;
        for (int i = 0; i < colCount; i++)
        {
            if (colWidths[i] <= 0) colWidths[i] = minColWidth;
            if (minWidths[i] <= 0) minWidths[i] = minColWidth;
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
        int colCount = result.Columns.Count;

        // Track rowspan: for each column, the remaining rows it is spanned
        var rowspanTracker = new (int remainingRows, float cellY, float height)[colCount];

        foreach (var row in table.Rows)
        {
            var rowLayout = new TableRowLayout
            {
                RowIndex = result.Rows.Count,
                Y = y,
                Height = row.Height > 0 ? row.Height : 20f
            };

            // Track which columns are occupied by rowspan from previous rows
            var occupiedCols = new HashSet<int>();
            for (int c = 0; c < colCount; c++)
            {
                if (rowspanTracker[c].remainingRows > 0)
                {
                    occupiedCols.Add(c);
                    rowspanTracker[c] = (rowspanTracker[c].remainingRows - 1,
                        rowspanTracker[c].cellY, rowspanTracker[c].height);
                }
            }

            foreach (var cell in row.Cells)
            {
                int col = cell.ColumnIndex;
                int spanEnd = Math.Min(col + cell.ColSpan, colCount);

                // Skip if all columns in span are occupied by rowspan
                bool allOccupied = true;
                for (int c = col; c < spanEnd; c++)
                {
                    if (!occupiedCols.Contains(c)) { allOccupied = false; break; }
                }
                if (allOccupied) continue;

                // Check for overlap with another cell in the same row
                bool overlaps = false;
                foreach (var existing in rowLayout.Cells)
                {
                    int eColEnd = existing.ColumnIndex + existing.ColSpan;
                    int newColEnd = col + cell.ColSpan;
                    if (col < eColEnd && existing.ColumnIndex < newColEnd)
                    {
                        overlaps = true; break;
                    }
                }
                if (overlaps) continue;

                float cellX = 0;
                for (int c = 0; c < col && c < colCount; c++)
                    cellX += result.Columns[c].Width;

                float cellWidth = 0;
                for (int c = col; c < spanEnd && c < colCount; c++)
                    cellWidth += result.Columns[c].Width;

                rowLayout.Cells.Add(new TableCellLayout
                {
                    RowIndex = cell.RowIndex,
                    ColumnIndex = col,
                    ColSpan = cell.ColSpan,
                    RowSpan = cell.RowSpan,
                    X = cellX,
                    Y = y,
                    Width = cellWidth,
                    Height = rowLayout.Height,
                    TextContent = cell.TextContent,
                    Style = cell.ComputedStyle ?? cell.Style
                });

                // Track rowspan
                if (cell.RowSpan > 1)
                {
                    for (int c = col; c < spanEnd; c++)
                    {
                        rowspanTracker[c] = (cell.RowSpan - 1, y, rowLayout.Height);
                    }
                }
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
