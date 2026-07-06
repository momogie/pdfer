using NSubstitute;
using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.Typography;

namespace PdfEr.Core.Tests;

public class IntrinsicSizeCalculatorTests
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
            AdvanceWidths = Enumerable.Range(0, 128)
                .ToDictionary(i => (char)i, _ => advancePt),
        };
        registry.GetMetrics(Arg.Any<string>(), Arg.Any<FontStyle>(), Arg.Any<float>()).Returns(metrics);
        return registry;
    }

    private static LayoutBox TextBox(string text, CssDeclarationBlock? decl = null) => new()
    {
        Kind = LayoutBoxKind.Text,
        TextContent = text,
        Style = ComputedStyle.Resolve(decl ?? new CssDeclarationBlock()),
    };

    private static LayoutBox ContainerBox(LayoutBoxKind kind, params LayoutBox[] children)
    {
        var box = new LayoutBox { Kind = kind, Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };
        foreach (var child in children)
            box.AddChild(child);
        return box;
    }

    [Fact]
    public void Calculate_SingleWordText_MinEqualsMaxContentWidth()
    {
        var registry = FakeRegistryWithFixedAdvance(5f); // 5pt per character
        var calc = new IntrinsicSizeCalculator(registry);
        var box = TextBox("hello"); // 5 chars

        var sizes = calc.Calculate(box);

        var expectedMm = 5 * 5f * PtToMm;
        Assert.Equal(expectedMm, sizes.MinContentWidth, 3);
        Assert.Equal(expectedMm, sizes.MaxContentWidth, 3);
    }

    [Fact]
    public void Calculate_MultiWordText_MinContentIsWidestWord()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var calc = new IntrinsicSizeCalculator(registry);
        var box = TextBox("a bb ccc"); // words: 1, 2, 3 chars; total incl. spaces = 8 chars

        var sizes = calc.Calculate(box);

        // min-content = widest unbreakable word = "ccc" = 3 chars
        Assert.Equal(3 * 5f * PtToMm, sizes.MinContentWidth, 3);
        // max-content = whole string on one line = 8 chars (a + space + bb + space + ccc)
        Assert.Equal(8 * 5f * PtToMm, sizes.MaxContentWidth, 3);
    }

    [Fact]
    public void Calculate_EmptyText_ReturnsZero()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var calc = new IntrinsicSizeCalculator(registry);
        var box = TextBox("");

        var sizes = calc.Calculate(box);

        Assert.Equal(0, sizes.MinContentWidth);
        Assert.Equal(0, sizes.MaxContentWidth);
    }

    [Fact]
    public void Calculate_MissingFontMetrics_FallsBackToApproximation()
    {
        var registry = Substitute.For<IFontRegistry>();
        registry.GetMetrics(Arg.Any<string>(), Arg.Any<FontStyle>(), Arg.Any<float>())
            .Returns((FontMetrics?)null);
        var calc = new IntrinsicSizeCalculator(registry);
        var box = TextBox("hello");

        var sizes = calc.Calculate(box);

        // Falls back to fontSizeMm * 0.5 per character (default 10pt -> mm), never zero.
        Assert.True(sizes.MaxContentWidth > 0);
        Assert.True(sizes.MinContentWidth > 0);
    }

    [Fact]
    public void Calculate_BlockContainer_TakesMaxOfChildrenWidths()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var calc = new IntrinsicSizeCalculator(registry);

        var shortText = TextBox("ab");   // 2 chars
        var longText = TextBox("abcdef"); // 6 chars
        var container = ContainerBox(LayoutBoxKind.Block, shortText, longText);

        var sizes = calc.Calculate(container);

        // Each text child sits on its own conceptual line in a block context,
        // so container intrinsic width = widest child, not the sum.
        Assert.Equal(6 * 5f * PtToMm, sizes.MaxContentWidth, 3);
    }

    [Fact]
    public void Calculate_InlineContainer_SumsChildrenForMaxContent()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var calc = new IntrinsicSizeCalculator(registry);

        var first = TextBox("abc"); // 3 chars
        var second = TextBox("de"); // 2 chars
        var container = ContainerBox(LayoutBoxKind.Anonymous, first, second);

        var sizes = calc.Calculate(container);

        // Inline-level children flow on the same line, so max-content is additive.
        Assert.Equal(5 * 5f * PtToMm, sizes.MaxContentWidth, 3);
    }

    [Fact]
    public void Calculate_ContainerWithPadding_AddsPaddingToContentWidth()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var calc = new IntrinsicSizeCalculator(registry);

        var decl = new CssDeclarationBlock();
        decl.SetProperty("padding-left", "2mm");
        decl.SetProperty("padding-right", "3mm");

        var text = TextBox("ab"); // 2 chars = 10pt = 3.528mm
        var container = new LayoutBox { Kind = LayoutBoxKind.Block, Style = ComputedStyle.Resolve(decl) };
        container.AddChild(text);

        var sizes = calc.Calculate(container);

        var expectedContentMm = 2 * 5f * PtToMm;
        Assert.Equal(expectedContentMm + 5f, sizes.MaxContentWidth, 3); // +2mm +3mm padding
    }

    [Fact]
    public void Calculate_ContainerWithNoChildren_ReturnsZero()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var calc = new IntrinsicSizeCalculator(registry);
        var box = new LayoutBox { Kind = LayoutBoxKind.Block, Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };

        var sizes = calc.Calculate(box);

        Assert.Equal(0, sizes.MinContentWidth);
        Assert.Equal(0, sizes.MaxContentWidth);
    }

    [Fact]
    public void Calculate_StoresResultOnBoxIntrinsicProperty()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var calc = new IntrinsicSizeCalculator(registry);
        var box = TextBox("hi");

        calc.Calculate(box);

        Assert.NotNull(box.Intrinsic);
        Assert.Equal(box.Intrinsic!.Value.MaxContentWidth, box.Intrinsic!.Value.MaxContentWidth);
    }

    [Fact]
    public void Calculate_NestedTree_PropagatesToAllDescendants()
    {
        var registry = FakeRegistryWithFixedAdvance(5f);
        var calc = new IntrinsicSizeCalculator(registry);

        var leaf = TextBox("abc");
        var middle = ContainerBox(LayoutBoxKind.Block, leaf);
        var root = ContainerBox(LayoutBoxKind.Block, middle);

        calc.Calculate(root);

        Assert.NotNull(leaf.Intrinsic);
        Assert.NotNull(middle.Intrinsic);
        Assert.NotNull(root.Intrinsic);
        Assert.Equal(leaf.Intrinsic!.Value.MaxContentWidth, root.Intrinsic!.Value.MaxContentWidth);
    }
}
