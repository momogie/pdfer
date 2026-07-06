using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Tests;

public class BlockPlacerTableTests
{
    private static LayoutBox TableBox(params LayoutBox[] rows)
    {
        var box = new LayoutBox { Kind = LayoutBoxKind.Table, TagName = "table", Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };
        foreach (var row in rows)
            box.AddChild(row);
        return box;
    }

    private static LayoutBox RowBox(params LayoutBox[] cells)
    {
        var box = new LayoutBox { Kind = LayoutBoxKind.TableRow, TagName = "tr", Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };
        foreach (var cell in cells)
            box.AddChild(cell);
        return box;
    }

    private static LayoutBox CellBox(CssDeclarationBlock? decl = null)
    {
        var box = new LayoutBox { Kind = LayoutBoxKind.TableCell, TagName = "td", Style = ComputedStyle.Resolve(decl ?? new CssDeclarationBlock()) };
        return box;
    }

    private static LayoutBox CellBoxWithHeight(float heightMm)
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("height", $"{heightMm}mm");
        return CellBox(decl);
    }

    [Fact]
    public void Place_SimpleTwoByTwoTable_ProducesTwoRowsWithTwoCellsEach()
    {
        var table = TableBox(
            RowBox(CellBox(), CellBox()),
            RowBox(CellBox(), CellBox()));

        var placer = new BlockPlacer();
        placer.Place(table, new ContainingBlock(100, 0, false), 0, 0);

        Assert.NotNull(table.Grid);
        Assert.Equal(2, table.Grid!.ColumnCount);
    }

    [Fact]
    public void Place_TableWithEqualWidthPlaceholder_DividesContainingBlockEvenly()
    {
        var table = TableBox(RowBox(CellBox(), CellBox()));

        var placer = new BlockPlacer();
        placer.Place(table, new ContainingBlock(100, 0, false), 0, 0);

        Assert.Equal(50, table.Grid!.ColumnWidths[0], 3);
        Assert.Equal(50, table.Grid.ColumnWidths[1], 3);
    }

    [Fact]
    public void Place_TableCells_ArePositionedSideBySideNotStacked()
    {
        var cellA = CellBox();
        var cellB = CellBox();
        var table = TableBox(RowBox(cellA, cellB));

        var placer = new BlockPlacer();
        placer.Place(table, new ContainingBlock(100, 0, false), 0, 0);

        Assert.Equal(0, cellA.Geometry.X, 3);
        Assert.Equal(50, cellB.Geometry.X, 3);
        Assert.Equal(cellA.Geometry.Y, cellB.Geometry.Y, 3); // same row, same Y
    }

    [Fact]
    public void Place_MultipleRows_StackVerticallyByRowHeight()
    {
        var row1Cell = CellBoxWithHeight(10);
        var row2Cell = CellBoxWithHeight(15);
        var table = TableBox(RowBox(row1Cell), RowBox(row2Cell));

        var placer = new BlockPlacer();
        placer.Place(table, new ContainingBlock(100, 0, false), 0, 0);

        Assert.Equal(0, row1Cell.Geometry.Y, 3);
        Assert.Equal(10, row2Cell.Geometry.Y, 3); // starts right after row 1's height
    }

    [Fact]
    public void Place_RowHeight_IsMaxOfItsCells()
    {
        var shortCell = CellBoxWithHeight(5);
        var tallCell = CellBoxWithHeight(20);
        var table = TableBox(RowBox(shortCell, tallCell));

        var placer = new BlockPlacer();
        placer.Place(table, new ContainingBlock(100, 0, false), 0, 0);

        // Both cells in the row stretch to the row's height (max of the two).
        Assert.Equal(20, shortCell.Geometry.Height, 3);
        Assert.Equal(20, tallCell.Geometry.Height, 3);
    }

    [Fact]
    public void Place_TableWithNoRows_FallsBackToPlainBlockWithoutCrashing()
    {
        var table = TableBox(); // no rows at all

        var placer = new BlockPlacer();
        var exception = Record.Exception(() => placer.Place(table, new ContainingBlock(100, 0, false), 0, 0));

        Assert.Null(exception);
        Assert.Null(table.Grid); // never entered the table-placement branch
    }

    [Fact]
    public void Place_TableWithRowsWrappedInAnonymousBlock_StillFindsRows()
    {
        // Mirrors BoxTreeBuilder's real shape: thead/tbody fall through to
        // LayoutBoxKind.Block (KindFromDisplay has no special case for
        // table-row-group etc.), so rows are nested one level deeper than
        // a naive "Table's direct children are rows" assumption would expect.
        var tbodyWrapper = new LayoutBox { Kind = LayoutBoxKind.Block, TagName = "tbody", Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };
        var cell = CellBox();
        tbodyWrapper.AddChild(RowBox(cell));

        var table = new LayoutBox { Kind = LayoutBoxKind.Table, TagName = "table", Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };
        table.AddChild(tbodyWrapper);

        var placer = new BlockPlacer();
        placer.Place(table, new ContainingBlock(100, 0, false), 0, 0);

        Assert.NotNull(table.Grid);
        Assert.Equal(1, table.Grid!.ColumnCount);
    }
}
