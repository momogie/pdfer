using NSubstitute;
using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.Typography;

namespace PdfEr.Core.Tests;

public class BlockPlacerLineBreakingTests
{
    private const float PtToMm = 0.3528f;

    private static IFontRegistry FakeRegistryWithFixedAdvance(float advancePt)
    {
        var registry = Substitute.For<IFontRegistry>();
        var metrics = new FontMetrics
        {
            FamilyName = "Test",
            Style = FontStyle.Regular,
            SizePoints = 10,
            AdvanceWidths = Enumerable.Range(0, 128).ToDictionary(i => (char)i, _ => advancePt),
        };
        registry.GetMetrics(Arg.Any<string>(), Arg.Any<FontStyle>(), Arg.Any<float>()).Returns(metrics);
        return registry;
    }

    private static LayoutBox TextBox(string text) => new()
    {
        Kind = LayoutBoxKind.Text,
        TextContent = text,
        Style = ComputedStyle.Resolve(new CssDeclarationBlock()),
    };

    private static LayoutBox AnonymousBlock(params LayoutBox[] children)
    {
        var box = new LayoutBox { Kind = LayoutBoxKind.Anonymous, IsAnonymous = true, Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };
        foreach (var child in children)
            box.AddChild(child);
        return box;
    }

    private static LayoutBox AnonymousBlockWithAlign(string textAlign, params LayoutBox[] children)
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("text-align", textAlign);
        var box = new LayoutBox { Kind = LayoutBoxKind.Anonymous, IsAnonymous = true, Style = ComputedStyle.Resolve(decl) };
        foreach (var child in children)
            box.AddChild(child);
        return box;
    }

    private static LayoutBox AnonymousBlockWithIndent(string textIndent, params LayoutBox[] children)
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("text-indent", textIndent);
        var box = new LayoutBox { Kind = LayoutBoxKind.Anonymous, IsAnonymous = true, Style = ComputedStyle.Resolve(decl) };
        foreach (var child in children)
            box.AddChild(child);
        return box;
    }

    private static LayoutBox AnonymousBlockWithWhiteSpace(string whiteSpace, params LayoutBox[] children)
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("white-space", whiteSpace);
        var box = new LayoutBox { Kind = LayoutBoxKind.Anonymous, IsAnonymous = true, Style = ComputedStyle.Resolve(decl) };
        foreach (var child in children)
            box.AddChild(child);
        return box;
    }

    private static LayoutBox TextBoxWithWhiteSpace(string text, string whiteSpace)
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("white-space", whiteSpace);
        var box = new LayoutBox { Kind = LayoutBoxKind.Text, TextContent = text, Style = ComputedStyle.Resolve(decl) };
        box.Intrinsic = new IntrinsicSizes(0, 0);
        return box;
    }

    private static List<LayoutBox> TextLinesOf(LayoutBox box) =>
        box.Children.Where(c => c.Kind == LayoutBoxKind.Text).ToList();

    [Fact]
    public void Place_ShortTextFittingOneLine_ProducesSingleLine()
    {
        var registry = FakeRegistryWithFixedAdvance(5f); // 5pt/char = 1.764mm/char
        var placer = new BlockPlacer(registry);
        var box = AnonymousBlock(TextBox("hi there"));

        // "hi" = 2 chars, "there" = 5 chars; each word individually fits easily
        // in a wide containing block, so both stay on one line.
        placer.Place(box, new ContainingBlock(200, 0, false), 0, 0);

        var lines = TextLinesOf(box);
        Assert.Equal(2, lines.Count); // two Word items ("hi", "there"), same Y (one line)
        Assert.Equal(lines[0].Geometry.Y, lines[1].Geometry.Y);
    }

    [Fact]
    public void Place_TextWiderThanContainingBlock_WrapsToMultipleLines()
    {
        var registry = FakeRegistryWithFixedAdvance(5f); // 1.764mm/char
        var placer = new BlockPlacer(registry);
        // Each word is 5 chars = 8.82mm. Container content width 20mm: fits ~2
        // words/line with the ~28% font-size space gap factored in.
        var box = AnonymousBlock(TextBox("alpha bravo charlie delta"));

        placer.Place(box, new ContainingBlock(20, 0, false), 0, 0);

        var lines = TextLinesOf(box);
        var distinctYs = lines.Select(l => l.Geometry.Y).Distinct().Count();
        Assert.True(distinctYs > 1, "Expected text to wrap across more than one line");
    }

    [Fact]
    public void Place_WrappedLines_StackVerticallyByLineHeight()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        var box = AnonymousBlock(TextBox("alpha bravo charlie delta echo foxtrot"));

        placer.Place(box, new ContainingBlock(15, 0, false), 0, 0);

        var lines = TextLinesOf(box);
        var distinctYs = lines.Select(l => l.Geometry.Y).Distinct().OrderBy(v => v).ToList();
        Assert.True(distinctYs.Count >= 2);

        // Each line's Y should increase by roughly one line-height step.
        for (int i = 1; i < distinctYs.Count; i++)
            Assert.True(distinctYs[i] > distinctYs[i - 1]);
    }

    [Fact]
    public void Place_LineBreak_ForcesNewLineRegardlessOfWidth()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        var lineBreak = new LayoutBox { Kind = LayoutBoxKind.LineBreak, Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };
        var box = AnonymousBlock(TextBox("short"), lineBreak, TextBox("text"));

        // Wide enough that both words would otherwise fit on one line.
        placer.Place(box, new ContainingBlock(500, 0, false), 0, 0);

        var lines = TextLinesOf(box);
        Assert.Equal(2, lines.Count);
        Assert.NotEqual(lines[0].Geometry.Y, lines[1].Geometry.Y);
    }

    [Fact]
    public void Place_SingleWordWiderThanContainer_DoesNotProduceEmptyFirstLine()
    {
        // CSS line-breaking never produces an empty line for an overlong word --
        // it overflows the first (only) line instead of being pushed down forever.
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        var box = AnonymousBlock(TextBox("supercalifragilisticexpialidocious"));

        placer.Place(box, new ContainingBlock(5, 0, false), 0, 0);

        var lines = TextLinesOf(box);
        var line = Assert.Single(lines);
        Assert.Equal(0, line.Geometry.Y);
    }

    [Fact]
    public void Place_ImageAmongText_TreatedAsAtomicInlineItem()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        var image = new LayoutBox
        {
            Kind = LayoutBoxKind.Image,
            Style = ComputedStyle.Resolve(new CssDeclarationBlock()),
            ImagePixelWidth = 96, // 1 inch at 96 DPI = 25.4mm
            ImagePixelHeight = 96,
        };
        var box = AnonymousBlock(TextBox("caption"), image);

        placer.Place(box, new ContainingBlock(200, 0, false), 0, 0);

        var imageChild = box.Children.Single(c => c.Kind == LayoutBoxKind.Image);
        Assert.Equal(25.4f, imageChild.Geometry.Width, 2);
    }

    [Fact]
    public void Place_TextAlignLeft_Default_StartsAtContentLeft()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        var box = AnonymousBlock(TextBox("hi")); // 2 chars = 1.764mm

        placer.Place(box, new ContainingBlock(50, 0, false), 10, 0);

        var line = TextLinesOf(box).Single();
        Assert.Equal(10, line.Geometry.X, 3);
    }

    [Fact]
    public void Place_TextAlignRight_ShiftsLineToRightEdge()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        var box = AnonymousBlockWithAlign("right", TextBox("hi")); // 2 chars = 1.764mm

        placer.Place(box, new ContainingBlock(50, 0, false), 0, 0);

        var line = TextLinesOf(box).Single();
        var expectedX = 50 - (2 * 5f * PtToMm);
        Assert.Equal(expectedX, line.Geometry.X, 2);
    }

    [Fact]
    public void Place_TextAlignCenter_CentersLineHorizontally()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        var box = AnonymousBlockWithAlign("center", TextBox("hi")); // 2 chars = 1.764mm

        placer.Place(box, new ContainingBlock(50, 0, false), 0, 0);

        var line = TextLinesOf(box).Single();
        var wordWidth = 2 * 5f * PtToMm;
        var expectedX = (50 - wordWidth) / 2f;
        Assert.Equal(expectedX, line.Geometry.X, 2);
    }

    [Fact]
    public void Place_TextAlignJustify_StretchesSpaceBetweenWordsOnNonLastLine()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        // Two words short enough to fit one line easily; justify should still
        // only apply to non-last lines -- with only one line, it IS the last
        // line, so words should NOT be stretched (stay at natural position).
        var box = AnonymousBlockWithAlign("justify", TextBox("hi there"));

        placer.Place(box, new ContainingBlock(50, 0, false), 0, 0);

        var lines = TextLinesOf(box);
        Assert.Equal(2, lines.Count);
        // "hi" at natural start position (no stretching on the last/only line).
        Assert.Equal(0, lines[0].Geometry.X, 2);
    }

    [Fact]
    public void Place_TextAlignJustify_NonLastLineFillsFullWidth()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        // Force a wrap so there are at least 2 lines; the first (non-last) line
        // should be justified to fill the full available width.
        var box = AnonymousBlockWithAlign("justify", TextBox("alpha bravo charlie delta echo foxtrot"));

        placer.Place(box, new ContainingBlock(15, 0, false), 0, 0);

        var lines = TextLinesOf(box);
        var lineGroups = lines.GroupBy(l => l.Geometry.Y).OrderBy(g => g.Key).ToList();
        Assert.True(lineGroups.Count >= 2, "Expected at least 2 lines to test justify on a non-last line");

        var firstLineItems = lineGroups[0].OrderBy(l => l.Geometry.X).ToList();
        if (firstLineItems.Count > 1)
        {
            var lastItem = firstLineItems[^1];
            var rightEdge = lastItem.Geometry.X + lastItem.Geometry.Width;
            Assert.Equal(15, rightEdge, 1); // justified line's last word ends at the content edge
        }
    }

    [Fact]
    public void Place_TextIndent_ShiftsFirstLineOnly()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        // Force a wrap so there's a second line whose start X we can compare
        // against the (indented) first line.
        var box = AnonymousBlockWithIndent("10mm", TextBox("alpha bravo charlie delta echo"));

        placer.Place(box, new ContainingBlock(15, 0, false), 0, 0);

        var lines = TextLinesOf(box);
        var lineGroups = lines.GroupBy(l => l.Geometry.Y).OrderBy(g => g.Key).ToList();
        Assert.True(lineGroups.Count >= 2, "Expected wrapping to produce a second line to compare against");

        var firstLineFirstItem = lineGroups[0].OrderBy(l => l.Geometry.X).First();
        var secondLineFirstItem = lineGroups[1].OrderBy(l => l.Geometry.X).First();

        Assert.Equal(10, firstLineFirstItem.Geometry.X, 2); // shifted by text-indent
        Assert.Equal(0, secondLineFirstItem.Geometry.X, 2); // not shifted
    }

    [Fact]
    public void Place_NoTextIndent_FirstLineNotShifted()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        var box = AnonymousBlock(TextBox("hi"));

        placer.Place(box, new ContainingBlock(100, 0, false), 0, 0);

        var line = TextLinesOf(box).Single();
        Assert.Equal(0, line.Geometry.X, 3);
    }

    [Fact]
    public void Place_NegativeTextIndent_OutdentsFirstLine()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        var box = AnonymousBlockWithIndent("-5mm", TextBox("hi"));

        placer.Place(box, new ContainingBlock(100, 0, false), 10, 0);

        var line = TextLinesOf(box).Single();
        Assert.Equal(5, line.Geometry.X, 2); // 10 (start x) - 5mm indent
    }

    [Fact]
    public void Place_WhiteSpaceNowrap_DoesNotWrapEvenWhenTooWide()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        var box = AnonymousBlockWithWhiteSpace("nowrap", TextBox("alpha bravo charlie delta echo"));

        placer.Place(box, new ContainingBlock(15, 0, false), 0, 0);

        var lines = TextLinesOf(box);
        var distinctYs = lines.Select(l => l.Geometry.Y).Distinct().Count();
        Assert.Equal(1, distinctYs); // everything stays on one line despite the narrow container
    }

    [Fact]
    public void Place_WhiteSpaceNowrap_StillBreaksOnExplicitLineBreak()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        var lineBreak = new LayoutBox { Kind = LayoutBoxKind.LineBreak, Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };
        var box = AnonymousBlockWithWhiteSpace("nowrap", TextBox("short"), lineBreak, TextBox("text"));

        placer.Place(box, new ContainingBlock(500, 0, false), 0, 0);

        var lines = TextLinesOf(box);
        var distinctYs = lines.Select(l => l.Geometry.Y).Distinct().Count();
        Assert.Equal(2, distinctYs); // <br> still forces a break under nowrap
    }

    [Fact]
    public void Place_WhiteSpacePreLine_PreservesSourceNewlinesAsForcedBreaks()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        var text = TextBoxWithWhiteSpace("line one\nline two", "pre-line");
        var box = AnonymousBlock(text);

        // Wide enough that width-based wrapping would never kick in on its own.
        placer.Place(box, new ContainingBlock(500, 0, false), 0, 0);

        var lines = TextLinesOf(box);
        var distinctYs = lines.Select(l => l.Geometry.Y).Distinct().Count();
        Assert.Equal(2, distinctYs); // the \n forced a break even though it fits on one line
    }

    [Fact]
    public void Place_WhiteSpacePreLine_StillCollapsesRunsOfSpaces()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var placer = new BlockPlacer(registry);
        // Multiple spaces between words should still collapse to a single gap
        // under pre-line -- only newlines are special-cased.
        var text = TextBoxWithWhiteSpace("hi   there", "pre-line");
        var box = AnonymousBlock(text);

        placer.Place(box, new ContainingBlock(500, 0, false), 0, 0);

        var lines = TextLinesOf(box);
        Assert.Equal(2, lines.Count); // "hi" and "there" as two separate word items
        Assert.Single(lines.Select(l => l.Geometry.Y).Distinct()); // both on the same line
    }

    [Fact]
    public void Place_NoFontRegistry_FallsBackToPlaceholderSingleLine()
    {
        // Backward-compat: without a font registry, BlockPlacer keeps its
        // original single-placeholder-line behavior rather than throwing.
        var placer = new BlockPlacer(); // fontRegistry: null
        var text = new LayoutBox
        {
            Kind = LayoutBoxKind.Text,
            TextContent = "hello world",
            Style = ComputedStyle.Resolve(new CssDeclarationBlock()),
        };
        var box = AnonymousBlock(text);

        placer.Place(box, new ContainingBlock(10, 0, false), 0, 0);

        // The anonymous box's only child remains a single un-split Text box
        // (the old behavior placed each original child as its own line).
        Assert.Single(box.Children);
    }
}
