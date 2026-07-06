using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Tests;

public class BoxTreePaintAdapterTests
{
    private static LayoutBox NewBox(LayoutBoxKind kind, string? tagName = null, string? text = null, CssDeclarationBlock? decl = null)
    {
        return new LayoutBox
        {
            Kind = kind,
            TagName = tagName,
            TextContent = text,
            Style = ComputedStyle.Resolve(decl ?? new CssDeclarationBlock()),
        };
    }

    [Fact]
    public void Adapt_RootBlock_CopiesGeometryFields()
    {
        var box = NewBox(LayoutBoxKind.Block, "div");
        box.Geometry = new BoxGeometry
        {
            X = 5, Y = 10, Width = 100, Height = 50,
            PaddingTop = 1, PaddingBottom = 2, PaddingLeft = 3, PaddingRight = 4,
            BorderTop = 0.5f, BorderBottom = 0.5f, BorderLeft = 0.25f, BorderRight = 0.25f,
            MarginTop = 6, MarginBottom = 7, MarginLeft = 8, MarginRight = 9,
        };

        var adapter = new BoxTreePaintAdapter();
        var result = adapter.Adapt(box);

        Assert.Equal("div", result.TagName);
        Assert.Equal(5, result.X);
        Assert.Equal(10, result.Y);
        Assert.Equal(100, result.Width);
        Assert.Equal(50, result.Height);
        Assert.Equal(1, result.PaddingTop);
        Assert.Equal(4, result.PaddingRight);
        Assert.Equal(0.5f, result.BorderTop);
        Assert.Equal(6, result.MarginTop);
        Assert.Equal(9, result.MarginRight);
    }

    [Fact]
    public void Adapt_NestedBlockChild_BecomesNestedBlockBox()
    {
        var child = NewBox(LayoutBoxKind.Block, "p");
        var root = NewBox(LayoutBoxKind.Block, "div");
        root.AddChild(child);

        var adapter = new BoxTreePaintAdapter();
        var result = adapter.Adapt(root);

        var childBlock = Assert.Single(result.Children);
        Assert.Equal("p", childBlock.TagName);
    }

    [Fact]
    public void Adapt_TextChild_BecomesInlineBoxOnParent_NotNestedBlockBox()
    {
        var text = NewBox(LayoutBoxKind.Text, text: "Hello world");
        text.Geometry = new BoxGeometry { X = 1, Y = 2, Width = 30, Height = 5 };
        var root = NewBox(LayoutBoxKind.Block, "p");
        root.AddChild(text);

        var adapter = new BoxTreePaintAdapter();
        var result = adapter.Adapt(root);

        Assert.Empty(result.Children); // no nested BlockBox for the text
        var inline = Assert.Single(result.InlineContent);
        Assert.Equal("Hello world", inline.Text);
        Assert.Equal(InlineBoxType.Text, inline.Type);
        Assert.Equal(1, inline.X);
        Assert.Equal(30, inline.Width);
    }

    [Fact]
    public void Adapt_AnonymousBlockWrappingInlineRun_FlattensTextOntoGrandparent()
    {
        // Mirrors BoxTreeBuilder's anonymous-block wrapping: <body>Some text<div>...
        // produces an Anonymous box wrapping a Text box, as a sibling of a real div.
        var text = NewBox(LayoutBoxKind.Text, text: "Some text");
        var anon = NewBox(LayoutBoxKind.Anonymous);
        anon.IsAnonymous = true;
        anon.AddChild(text);

        var div = NewBox(LayoutBoxKind.Block, "div");

        var body = NewBox(LayoutBoxKind.Block, "body");
        body.AddChild(anon);
        body.AddChild(div);

        var adapter = new BoxTreePaintAdapter();
        var result = adapter.Adapt(body);

        // The anonymous box produces no BlockBox of its own -- only "div" does.
        var childBlock = Assert.Single(result.Children);
        Assert.Equal("div", childBlock.TagName);

        // Its text ends up as inline content directly on "body".
        var inline = Assert.Single(result.InlineContent);
        Assert.Equal("Some text", inline.Text);
    }

    [Fact]
    public void Adapt_ComputedStyleDeclarations_ArePassedThrough()
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("color", "#ff0000");
        var box = NewBox(LayoutBoxKind.Block, "div", decl: decl);

        var adapter = new BoxTreePaintAdapter();
        var result = adapter.Adapt(box);

        Assert.Equal("#ff0000", result.ComputedStyle?.GetPropertyValue("color"));
    }

    [Fact]
    public void Adapt_MultipleBlockChildren_PreserveOrder()
    {
        var first = NewBox(LayoutBoxKind.Block, "h1");
        var second = NewBox(LayoutBoxKind.Block, "p");
        var root = NewBox(LayoutBoxKind.Block, "body");
        root.AddChild(first);
        root.AddChild(second);

        var adapter = new BoxTreePaintAdapter();
        var result = adapter.Adapt(root);

        Assert.Equal(2, result.Children.Count);
        Assert.Equal("h1", result.Children[0].TagName);
        Assert.Equal("p", result.Children[1].TagName);
    }
}
