using PdfEr.Core.Application.HtmlProcessing;

namespace PdfEr.Core.Tests;

public class CssLengthParserTests
{
    [Theory]
    [InlineData(null, 0f)]
    [InlineData("", 0f)]
    [InlineData("0", 0f)]
    [InlineData("0px", 0f)]
    [InlineData("auto", 0f)]
    [InlineData("none", 0f)]
    [InlineData("thin", 0.5f)]
    [InlineData("medium", 1f)]
    [InlineData("thick", 2f)]
    [InlineData("5mm", 5f)]
    [InlineData("10pt", 3.528f)]
    [InlineData("10px", 2.646f)]
    [InlineData("1cm", 10f)]
    [InlineData("1in", 25.4f)]
    [InlineData("2rem", 7.056f)] // 2 * 10 * 0.3528
    [InlineData("2em", 7.056f)]
    [InlineData("15", 15f)] // unitless number
    public void ParseLengthMm_MatchesKnownConversions(string? value, float expectedMm)
    {
        Assert.Equal(expectedMm, CssLengthParser.ParseLengthMm(value), 3);
    }

    [Fact]
    public void ParseCssLengthMm_Percent_ResolvesAgainstParentDimension()
    {
        Assert.Equal(50f, CssLengthParser.ParseCssLengthMm("50%", 100f), 3);
    }

    [Fact]
    public void ParseCssLengthMm_Auto_ReturnsZero()
    {
        Assert.Equal(0f, CssLengthParser.ParseCssLengthMm("auto", 100f));
    }

    [Fact]
    public void ParseCssLengthMm_AbsoluteLength_DelegatesToParseLengthMm()
    {
        Assert.Equal(5f, CssLengthParser.ParseCssLengthMm("5mm", 100f), 3);
    }
}
