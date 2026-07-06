using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Tests;

public class BlockPlacerTests
{
    private static LayoutBox Block(CssDeclarationBlock? decl = null, params LayoutBox[] children)
    {
        var box = new LayoutBox { Kind = LayoutBoxKind.Block, Style = ComputedStyle.Resolve(decl ?? new CssDeclarationBlock()) };
        foreach (var child in children)
            box.AddChild(child);
        return box;
    }

    private static LayoutBox TextBox(string text)
    {
        var box = new LayoutBox { Kind = LayoutBoxKind.Text, TextContent = text, Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };
        box.Intrinsic = new IntrinsicSizes(0, 0);
        return box;
    }

    [Fact]
    public void Place_RootBlock_FillsContainingBlockWidth()
    {
        var box = Block();
        var placer = new BlockPlacer();
        var cb = new ContainingBlock(180, 0, false);

        placer.Place(box, cb, 10, 20);

        Assert.Equal(180, box.Geometry.Width);
        Assert.Equal(10, box.Geometry.X);
        Assert.Equal(20, box.Geometry.Y);
    }

    [Fact]
    public void Place_EmptyBlock_HasZeroHeight()
    {
        var box = Block();
        var placer = new BlockPlacer();

        placer.Place(box, new ContainingBlock(100, 0, false), 0, 0);

        Assert.Equal(0, box.Geometry.Height);
    }

    [Fact]
    public void Place_TwoBlockChildren_StackVerticallyWithNoOverlap()
    {
        var decl1 = new CssDeclarationBlock();
        decl1.SetProperty("height", "10mm");
        var decl2 = new CssDeclarationBlock();
        decl2.SetProperty("height", "15mm");

        var child1 = Block(decl1);
        var child2 = Block(decl2);
        var root = Block(null, child1, child2);

        var placer = new BlockPlacer();
        placer.Place(root, new ContainingBlock(100, 0, false), 0, 0);

        Assert.Equal(0, child1.Geometry.Y);
        Assert.Equal(10, child1.Geometry.Height);
        Assert.Equal(10, child2.Geometry.Y); // starts right after child1, no gap
        Assert.Equal(15, child2.Geometry.Height);
        Assert.Equal(25, root.Geometry.Height); // sum of children heights
    }

    [Fact]
    public void Place_ChildWithMarginTopAndBottom_AddsSpaceWithoutCollapsing()
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("height", "10mm");
        decl.SetProperty("margin-top", "5mm");
        decl.SetProperty("margin-bottom", "3mm");

        var child = Block(decl);
        var root = Block(null, child);

        var placer = new BlockPlacer();
        placer.Place(root, new ContainingBlock(100, 0, false), 0, 0);

        Assert.Equal(5, child.Geometry.Y); // pushed down by margin-top
        Assert.Equal(10, child.Geometry.Height);
        // Root height = marginTop + height + marginBottom (no collapsing applied)
        Assert.Equal(18, root.Geometry.Height);
    }

    [Fact]
    public void Place_ExplicitWidthInMm_OverridesContainingBlockFill()
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("width", "50mm");
        var box = Block(decl);

        var placer = new BlockPlacer();
        placer.Place(box, new ContainingBlock(180, 0, false), 0, 0);

        Assert.Equal(50, box.Geometry.Width);
    }

    [Fact]
    public void Place_WidthAsPercent_ResolvesAgainstContainingBlock()
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("width", "50%");
        var box = Block(decl);

        var placer = new BlockPlacer();
        placer.Place(box, new ContainingBlock(180, 0, false), 0, 0);

        Assert.Equal(90, box.Geometry.Width);
    }

    [Fact]
    public void Place_HeightAsPercent_WithIndefiniteContainingBlock_IsIgnored()
    {
        // CSS 2.1 10.5: a percentage height against an indefinite containing
        // block height must be treated as auto (computed from content), not
        // resolved to a number.
        var decl = new CssDeclarationBlock();
        decl.SetProperty("height", "50%");
        var box = Block(decl);

        var placer = new BlockPlacer();
        placer.Place(box, new ContainingBlock(100, 0, false), 0, 0);

        Assert.Equal(0, box.Geometry.Height); // falls back to content height (no children = 0)
    }

    [Fact]
    public void Place_HeightAsPercent_WithDefiniteContainingBlock_Resolves()
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("height", "50%");
        var box = Block(decl);

        var placer = new BlockPlacer();
        placer.Place(box, new ContainingBlock(100, 200, true), 0, 0);

        Assert.Equal(100, box.Geometry.Height);
    }

    [Fact]
    public void Place_PaddingAndBorder_OffsetChildContentArea()
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("padding-left", "5mm");
        decl.SetProperty("padding-top", "3mm");
        decl.SetProperty("border-left-width", "1mm");
        decl.SetProperty("border-top-width", "1mm");

        var childDecl = new CssDeclarationBlock();
        childDecl.SetProperty("height", "10mm");
        var child = Block(childDecl);
        var root = Block(decl, child);

        var placer = new BlockPlacer();
        placer.Place(root, new ContainingBlock(100, 0, false), 0, 0);

        Assert.Equal(6, child.Geometry.X); // 5mm padding + 1mm border
        Assert.Equal(4, child.Geometry.Y); // 3mm padding + 1mm border
    }

    [Fact]
    public void Place_TextBox_UsesFontSizeTimesLineHeightFactor()
    {
        var box = TextBox("hello");
        var placer = new BlockPlacer();

        placer.Place(box, new ContainingBlock(100, 0, false), 0, 0);

        var expected = box.Style.FontSizeMm * 1.3f;
        Assert.Equal(expected, box.Geometry.Height, 3);
    }

    [Fact]
    public void Place_NestedBlocks_ChildContainingBlockShrinksByParentPadding()
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("padding-left", "10mm");
        decl.SetProperty("padding-right", "10mm");

        var child = Block();
        var root = Block(decl, child);

        var placer = new BlockPlacer();
        placer.Place(root, new ContainingBlock(100, 0, false), 0, 0);

        // Child fills the content width, not the full containing block width.
        Assert.Equal(80, child.Geometry.Width);
    }

    [Fact]
    public void Place_InlineBlockChild_ShrinksToFitIntrinsicMaxContent()
    {
        var textChild = TextBox("hi");
        textChild.Intrinsic = new IntrinsicSizes(5, 20); // min=5mm, max=20mm

        var inlineBlock = new LayoutBox { Kind = LayoutBoxKind.InlineBlock, Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };
        inlineBlock.AddChild(textChild);
        inlineBlock.Intrinsic = new IntrinsicSizes(5, 20);

        var placer = new BlockPlacer();
        placer.Place(inlineBlock, new ContainingBlock(100, 0, false), 0, 0);

        Assert.Equal(20, inlineBlock.Geometry.Width);
    }
}
