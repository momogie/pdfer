using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Tests;

public class ComputedStyleTests
{
    [Theory]
    [InlineData("10pt", 10f)]
    [InlineData("20px", 15f)]
    [InlineData("5mm", 14.173f)]
    [InlineData("2rem", 20f)]
    [InlineData("1.5em", 15f)]
    [InlineData("150%", 15f)]
    [InlineData("large", 12f)]
    [InlineData("medium", 10f)]
    [InlineData(null, 10f)]
    public void Resolve_FontSizePt_MatchesLayoutEngineGetFontSizePt(string? value, float expectedPt)
    {
        var decl = new CssDeclarationBlock();
        if (value != null)
            decl.SetProperty("font-size", value);

        var style = ComputedStyle.Resolve(decl);

        Assert.Equal(expectedPt, style.FontSizePt, 2);
    }

    [Fact]
    public void Resolve_FontSizeMm_IsPtTimesConversionFactor()
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("font-size", "10pt");

        var style = ComputedStyle.Resolve(decl);

        Assert.Equal(10f * 0.3528f, style.FontSizeMm, 4);
    }

    [Fact]
    public void Resolve_Display_DefaultsToInline()
    {
        var style = ComputedStyle.Resolve(new CssDeclarationBlock());
        Assert.Equal("inline", style.Display);
    }

    [Fact]
    public void Resolve_Display_ReadsPropertyLowercased()
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("display", "BLOCK");

        var style = ComputedStyle.Resolve(decl);

        Assert.Equal("block", style.Display);
    }

    [Fact]
    public void Resolve_Position_DefaultsToStatic()
    {
        var style = ComputedStyle.Resolve(new CssDeclarationBlock());
        Assert.Equal("static", style.Position);
    }

    [Fact]
    public void Resolve_Float_NoneNormalizesToNull()
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("float", "none");

        var style = ComputedStyle.Resolve(decl);

        Assert.Null(style.Float);
    }

    [Fact]
    public void Resolve_Float_LeftIsPreserved()
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("float", "left");

        var style = ComputedStyle.Resolve(decl);

        Assert.Equal("left", style.Float);
    }

    [Fact]
    public void GetPropertyValue_DelegatesToUnderlyingDeclarations()
    {
        var decl = new CssDeclarationBlock();
        decl.SetProperty("color", "#ff0000");

        var style = ComputedStyle.Resolve(decl);

        Assert.Equal("#ff0000", style.GetPropertyValue("color"));
    }
}

public class LayoutBoxTests
{
    [Fact]
    public void AddChild_SetsParentReference()
    {
        var parent = new LayoutBox { TagName = "div", Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };
        var child = new LayoutBox { TagName = "p", Style = ComputedStyle.Resolve(new CssDeclarationBlock()) };

        parent.AddChild(child);

        Assert.Single(parent.Children);
        Assert.Same(parent, child.Parent);
    }

    [Fact]
    public void BoxGeometry_ContentWidth_SubtractsPaddingAndBorder()
    {
        var geometry = new BoxGeometry
        {
            Width = 100,
            PaddingLeft = 5,
            PaddingRight = 5,
            BorderLeft = 1,
            BorderRight = 1
        };

        Assert.Equal(88, geometry.ContentWidth);
    }

    [Fact]
    public void BoxGeometry_TotalWidth_AddsMargins()
    {
        var geometry = new BoxGeometry
        {
            Width = 100,
            MarginLeft = 10,
            MarginRight = 10
        };

        Assert.Equal(120, geometry.TotalWidth);
    }
}
