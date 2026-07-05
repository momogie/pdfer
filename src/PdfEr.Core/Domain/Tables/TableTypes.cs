using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Domain.Tables;

public sealed class TableDefinition
{
    public List<TableRow> Rows { get; } = new();
    public int ColumnCount { get; set; }
    public float TotalWidth { get; set; }
    public CssDeclarationBlock? Style { get; set; }
    public List<float> FixedColWidths { get; } = new();
    public bool IsFixedLayout { get; set; }
}

public sealed class TableRow
{
    public List<TableCell> Cells { get; } = new();
    public bool IsHeader { get; set; }
    public bool IsFooter { get; set; }
    public float Height { get; set; }
    public CssDeclarationBlock? Style { get; set; }
}

public sealed class TableCell
{
    public string? TextContent { get; set; }
    public int ColumnIndex { get; set; }
    public int RowIndex { get; set; }
    public bool IsHeader { get; set; }
    public int ColSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
    public float Width { get; set; }
    public float Height { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public CssDeclarationBlock? Style { get; set; }
    public CssDeclarationBlock? ComputedStyle { get; set; }
}

public sealed class TableLayoutResult
{
    public List<TableColumnInfo> Columns { get; } = new();
    public List<TableRowLayout> Rows { get; } = new();
    public float TotalWidth { get; set; }
    public float TotalHeight { get; set; }
}

public sealed class TableColumnInfo
{
    public int Index { get; set; }
    public float Width { get; set; }
    public float MinWidth { get; set; }
    public float MaxWidth { get; set; }
}

public sealed class TableRowLayout
{
    public int RowIndex { get; set; }
    public float Height { get; set; }
    public float Y { get; set; }
    public List<TableCellLayout> Cells { get; } = new();
}

public sealed class TableCellLayout
{
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public int ColSpan { get; set; }
    public int RowSpan { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public string? TextContent { get; set; }
    public CssDeclarationBlock? Style { get; set; }
}
