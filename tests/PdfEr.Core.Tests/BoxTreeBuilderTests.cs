using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Tests;

public class BoxTreeBuilderTests
{
    private static async Task<LayoutBox?> BuildAsync(string html)
    {
        var htmlParser = new HtmlParser();
        var cssParser = new CssParser();
        var cssNormalizer = new CssNormalizer();
        var cssMerger = new CssMerger(cssParser, cssNormalizer);
        var builder = new BoxTreeBuilder(cssMerger, cssNormalizer);

        var parseResult = await htmlParser.ParseAsync(html);
        if (!string.IsNullOrWhiteSpace(parseResult.ExtractedCss))
            cssMerger.AddUserStylesheet(cssParser.Parse(parseResult.ExtractedCss));

        return builder.BuildFromDocument(parseResult.Document);
    }

    [Fact]
    public async Task BuildFromDocument_SimpleParagraph_ProducesBlockBoxWithTextChild()
    {
        var root = await BuildAsync("<html><body><p>Hello world</p></body></html>");

        Assert.NotNull(root);
        Assert.Equal("body", root!.TagName);
        Assert.Equal(LayoutBoxKind.Block, root.Kind);

        var p = Assert.Single(root.Children);
        Assert.Equal("p", p.TagName);
        Assert.Equal(LayoutBoxKind.Block, p.Kind);

        var text = Assert.Single(p.Children);
        Assert.Equal(LayoutBoxKind.Text, text.Kind);
        Assert.Equal("Hello world", text.TextContent);
    }

    [Fact]
    public async Task BuildFromDocument_NestedDivs_PreservesHierarchyAndParentLinks()
    {
        var root = await BuildAsync("<html><body><div><div><p>Deep</p></div></div></body></html>");

        var outerDiv = Assert.Single(root!.Children);
        Assert.Equal("div", outerDiv.TagName);

        var innerDiv = Assert.Single(outerDiv.Children);
        Assert.Equal("div", innerDiv.TagName);
        Assert.Same(outerDiv, innerDiv.Parent);

        var p = Assert.Single(innerDiv.Children);
        Assert.Equal("p", p.TagName);
        Assert.Same(innerDiv, p.Parent);
    }

    [Fact]
    public async Task BuildFromDocument_DisplayNoneElement_IsExcludedFromTree()
    {
        var root = await BuildAsync(
            "<html><body><p>Visible</p><div style=\"display:none\">Hidden</div></body></html>");

        Assert.Single(root!.Children);
        Assert.Equal("p", root.Children[0].TagName);
    }

    [Fact]
    public async Task BuildFromDocument_ScriptAndStyleTags_AreSkipped()
    {
        var root = await BuildAsync(
            "<html><body><script>var x = 1;</script><p>Text</p></body></html>");

        Assert.Single(root!.Children);
        Assert.Equal("p", root.Children[0].TagName);
    }

    [Fact]
    public async Task BuildFromDocument_MixedBlockAndInlineChildren_WrapsInlineRunInAnonymousBlock()
    {
        // <body> containing a bare text node followed by a <div> is a classic
        // CSS 2.1 §9.2.1.1 anonymous-block case: body is block-level, the text
        // is inline-level, so the text must be wrapped rather than left as a
        // direct sibling of the block-level <div>.
        var root = await BuildAsync("<html><body>Some text<div>Block content</div></body></html>");

        Assert.Equal(2, root!.Children.Count);

        var anon = root.Children[0];
        Assert.True(anon.IsAnonymous);
        Assert.Equal(LayoutBoxKind.Anonymous, anon.Kind);
        var textChild = Assert.Single(anon.Children);
        Assert.Equal(LayoutBoxKind.Text, textChild.Kind);
        Assert.Equal("Some text", textChild.TextContent);

        var div = root.Children[1];
        Assert.Equal("div", div.TagName);
        Assert.False(div.IsAnonymous);
    }

    [Fact]
    public async Task BuildFromDocument_AllInlineChildren_NoAnonymousWrapping()
    {
        // A block container whose children are ALL inline-level should not be
        // wrapped at all — anonymous boxes only appear for MIXED content.
        var root = await BuildAsync("<html><body><p>Hello <span>world</span></p></body></html>");

        var p = Assert.Single(root!.Children);
        Assert.All(p.Children, c => Assert.True(c.Kind is LayoutBoxKind.Text or LayoutBoxKind.Inline));
    }

    [Fact]
    public async Task BuildFromDocument_AllBlockChildren_NoAnonymousWrapping()
    {
        var root = await BuildAsync("<html><body><div>One</div><div>Two</div></body></html>");

        Assert.Equal(2, root!.Children.Count);
        Assert.All(root.Children, c => Assert.False(c.IsAnonymous));
    }

    [Fact]
    public async Task BuildFromDocument_EmptyBody_ReturnsRootWithNoChildren()
    {
        var root = await BuildAsync("<html><body></body></html>");

        Assert.NotNull(root);
        Assert.Empty(root!.Children);
    }
}
