using NSubstitute;
using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Tests;

public class BoxTreeBuilderTests
{
    private static async Task<LayoutBox?> BuildAsync(string html, IImageService? imageService = null)
    {
        var htmlParser = new HtmlParser();
        var cssParser = new CssParser();
        var cssNormalizer = new CssNormalizer();
        var cssMerger = new CssMerger(cssParser, cssNormalizer);
        var builder = new BoxTreeBuilder(cssMerger, cssNormalizer, imageService);

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

    [Fact]
    public async Task BuildFromDocument_UnorderedList_MarksEachItemWithBulletMarker()
    {
        var root = await BuildAsync("<html><body><ul><li>One</li><li>Two</li></ul></body></html>");

        var ul = Assert.Single(root!.Children);
        Assert.Equal(2, ul.Children.Count);
        Assert.All(ul.Children, li => Assert.Equal("•", li.ListMarker));
    }

    [Fact]
    public async Task BuildFromDocument_OrderedList_IncrementsMarkerPerItem()
    {
        var root = await BuildAsync("<html><body><ol><li>First</li><li>Second</li><li>Third</li></ol></body></html>");

        var ol = Assert.Single(root!.Children);
        Assert.Equal("1.", ol.Children[0].ListMarker);
        Assert.Equal("2.", ol.Children[1].ListMarker);
        Assert.Equal("3.", ol.Children[2].ListMarker);
    }

    [Fact]
    public async Task BuildFromDocument_OrderedListWithStartAttribute_BeginsAtStart()
    {
        var root = await BuildAsync("<html><body><ol start=\"5\"><li>Five</li><li>Six</li></ol></body></html>");

        var ol = Assert.Single(root!.Children);
        Assert.Equal("5.", ol.Children[0].ListMarker);
        Assert.Equal("6.", ol.Children[1].ListMarker);
    }

    [Fact]
    public async Task BuildFromDocument_NestedLists_EachTracksOwnCounter()
    {
        var root = await BuildAsync(
            "<html><body><ol><li>Outer 1</li><li>Outer 2<ol><li>Inner 1</li></ol></li></ol></body></html>");

        var outerOl = Assert.Single(root!.Children);
        Assert.Equal("1.", outerOl.Children[0].ListMarker);
        Assert.Equal("2.", outerOl.Children[1].ListMarker);

        var innerOl = outerOl.Children[1].Children.First(c => c.TagName == "ol");
        var innerLi = Assert.Single(innerOl.Children);
        Assert.Equal("1.", innerLi.ListMarker); // inner list starts its own counter at 1
    }

    [Fact]
    public async Task BuildFromDocument_AnchorWithHref_SetsLinkUrl()
    {
        var root = await BuildAsync("<html><body><a href=\"https://example.com\">Link</a></body></html>");

        // <a> is inline by default, so it ends up wrapped in an anonymous block
        // alongside other inline content -- find it by tag name instead of position.
        var anchor = FindByTagName(root!, "a");
        Assert.NotNull(anchor);
        Assert.Equal("https://example.com", anchor!.LinkUrl);
    }

    [Fact]
    public async Task BuildFromDocument_AnchorWithName_SetsAnchorName()
    {
        var root = await BuildAsync("<html><body><a name=\"section1\">Target</a></body></html>");

        var anchor = FindByTagName(root!, "a");
        Assert.NotNull(anchor);
        Assert.Equal("section1", anchor!.AnchorName);
    }

    [Fact]
    public async Task BuildFromDocument_LineBreak_ProducesLineBreakBox()
    {
        var root = await BuildAsync("<html><body><p>Line one<br>Line two</p></body></html>");

        var p = Assert.Single(root!.Children);
        Assert.Contains(p.Children, c => c.Kind == LayoutBoxKind.LineBreak);
    }

    [Fact]
    public async Task BuildFromDocument_ImageWithService_ProducesImageBoxWithPixelDimensions()
    {
        var imageService = Substitute.For<IImageService>();
        imageService.LoadImage(Arg.Any<string>(), Arg.Any<string?>()).Returns(new ImageLoadResult
        {
            Data = new byte[] { 1, 2, 3 },
            PixelWidth = 200,
            PixelHeight = 100,
        });

        var root = await BuildAsync("<html><body><img src=\"photo.png\"></body></html>", imageService);

        var img = FindByTagName(root!, "img");
        Assert.NotNull(img);
        Assert.Equal(LayoutBoxKind.Image, img!.Kind);
        Assert.Equal(200, img.ImagePixelWidth);
        Assert.Equal(100, img.ImagePixelHeight);
        Assert.Equal(3, img.ImageData!.Length);
    }

    [Fact]
    public async Task BuildFromDocument_ImageWithWidthHeightAttributes_OverridesNaturalSize()
    {
        var imageService = Substitute.For<IImageService>();
        imageService.LoadImage(Arg.Any<string>(), Arg.Any<string?>()).Returns(new ImageLoadResult
        {
            Data = new byte[] { 1 },
            PixelWidth = 200,
            PixelHeight = 100,
        });

        var root = await BuildAsync("<html><body><img src=\"photo.png\" width=\"50\" height=\"25\"></body></html>", imageService);

        var img = FindByTagName(root!, "img");
        Assert.Equal(50, img!.ImagePixelWidth);
        Assert.Equal(25, img.ImagePixelHeight);
    }

    [Fact]
    public async Task BuildFromDocument_ImageWithoutImageService_ProducesNoImageBox()
    {
        var root = await BuildAsync("<html><body><img src=\"photo.png\"></body></html>", imageService: null);

        Assert.DoesNotContain(root!.Children, c => c.TagName == "img");
        // Also check any nested/anonymous boxes -- there should be no image anywhere.
        Assert.Null(FindByTagName(root, "img"));
    }

    [Fact]
    public async Task BuildFromDocument_ImageWithMissingSrc_ProducesNoImageBox()
    {
        var imageService = Substitute.For<IImageService>();
        var root = await BuildAsync("<html><body><img></body></html>", imageService);

        imageService.DidNotReceive().LoadImage(Arg.Any<string>(), Arg.Any<string?>());
        Assert.Null(FindByTagName(root!, "img"));
    }

    [Fact]
    public async Task BuildFromDocument_TableCell_DefaultsToColSpanRowSpanOne()
    {
        var root = await BuildAsync("<html><body><table><tr><td>Cell</td></tr></table></body></html>");

        var cell = FindByTagName(root!, "td");
        Assert.NotNull(cell);
        Assert.Equal(1, cell!.ColSpan);
        Assert.Equal(1, cell.RowSpan);
    }

    [Fact]
    public async Task BuildFromDocument_TableCellWithColspan_ParsesAttribute()
    {
        var root = await BuildAsync("<html><body><table><tr><td colspan=\"3\">Wide</td></tr></table></body></html>");

        var cell = FindByTagName(root!, "td");
        Assert.Equal(3, cell!.ColSpan);
    }

    [Fact]
    public async Task BuildFromDocument_TableHeaderCellWithRowspan_ParsesAttribute()
    {
        var root = await BuildAsync("<html><body><table><tr><th rowspan=\"2\">Tall</th></tr></table></body></html>");

        var cell = FindByTagName(root!, "th");
        Assert.Equal(2, cell!.RowSpan);
    }

    [Fact]
    public async Task BuildFromDocument_TableCellWithInvalidColspan_FallsBackToOne()
    {
        var root = await BuildAsync("<html><body><table><tr><td colspan=\"notanumber\">Cell</td></tr></table></body></html>");

        var cell = FindByTagName(root!, "td");
        Assert.Equal(1, cell!.ColSpan);
    }

    [Fact]
    public async Task BuildFromDocument_TableElements_HaveTableLayoutBoxKinds()
    {
        var root = await BuildAsync("<html><body><table><tr><td>Cell</td></tr></table></body></html>");

        var table = FindByTagName(root!, "table");
        var row = FindByTagName(root!, "tr");
        var cell = FindByTagName(root!, "td");

        Assert.Equal(LayoutBoxKind.Table, table!.Kind);
        Assert.Equal(LayoutBoxKind.TableRow, row!.Kind);
        Assert.Equal(LayoutBoxKind.TableCell, cell!.Kind);
    }

    private static LayoutBox? FindByTagName(LayoutBox box, string tagName)
    {
        if (box.TagName == tagName) return box;
        foreach (var child in box.Children)
        {
            var found = FindByTagName(child, tagName);
            if (found != null) return found;
        }
        return null;
    }
}
