using SixLabors.ImageSharp.Processing;

namespace PdfEr.FidelityTests;

/// <summary>
/// Exploratory SSIM check for the box-tree pipeline (docs/plans/phase-01-foundation.md)
/// against Chromium ground truth, kept separate from the main fidelity harness
/// (FidelityTests.RunAllFidelityTests) because the box-tree pipeline is still an early,
/// partial implementation -- BoxTreeBuilder does not dispatch to ITagHandler, so it has
/// no list/table/flex/grid support and no pagination. Mixing its scores into the main
/// report's regression gate would conflate "the mature streaming pipeline regressed"
/// with "the box-tree pipeline is still incomplete," which are very different signals.
/// </summary>
public class BoxTreePipelineFidelityTests : IAsyncLifetime
{
    private PdfRenderingHelper? _helper;

    public async Task InitializeAsync()
    {
        _helper = await PdfRenderingHelper.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_helper != null)
            await _helper.DisposeAsync();
    }

    [Theory]
    [InlineData("<html><body><p>Hello box tree pipeline</p></body></html>")]
    [InlineData("<html><body><div><h1>Title</h1><p>Some paragraph text.</p></div></body></html>")]
    [InlineData("<html><body><ul><li>First item</li><li>Second item</li><li>Third item</li></ul></body></html>")]
    [InlineData("<html><body><ol><li>Alpha</li><li>Beta</li></ol></body></html>")]
    [InlineData("""
        <html><body><p style="width:80mm">This is a long paragraph of text that is
        definitely wide enough to require word-wrapping across multiple lines once
        the box-tree pipeline's real line-breaking is exercised against a narrow
        containing block width.</p></body></html>
        """)]
    [InlineData("""
        <html><body>
        <p style="width:80mm;text-align:center">Centered text should sit in the
        middle of this narrow paragraph once wrapped across lines.</p>
        <p style="width:80mm;text-align:right">Right aligned text should hug the
        right edge of this narrow paragraph once wrapped across lines.</p>
        <p style="width:80mm;text-align:justify">Justified text should stretch
        the spacing between words so each non-last line reaches the full width
        of this narrow paragraph once wrapped across lines.</p>
        </body></html>
        """)]
    [InlineData("""
        <html><body><div style="width:60mm;padding:10mm;box-sizing:border-box;border:1mm solid black">
        This div uses box-sizing:border-box, so its declared width already
        includes the padding and border.</div></body></html>
        """)]
    [InlineData("""
        <html><body>
        <div style="width:10mm;min-width:70mm;border:1px solid black">This div's
        declared width is overridden by a wider min-width.</div>
        <div style="max-width:50mm;border:1px solid black">This div would fill
        the page but is capped by max-width.</div>
        </body></html>
        """)]
    [InlineData("""
        <html><body><div style="width:80mm;margin:0 auto;border:1px solid black">
        This div is centered horizontally on the page via margin:0 auto.</div></body></html>
        """)]
    [InlineData("""
        <html><body>
        <p style="width:80mm;line-height:2">This paragraph has double line-height,
        so wrapped lines should be spaced twice as far apart as normal.</p>
        <p style="width:80mm;line-height:20px">This paragraph has an absolute
        line-height in pixels, not scaled by font-size at all.</p>
        </body></html>
        """)]
    public async Task BoxTreePipeline_SimpleDocument_ProducesComparableOutput(string html)
    {
        var pdfChromium = await _helper!.RenderHtmlViaChromiumAsync(html);
        var pdfBoxTree = _helper.RenderHtmlViaPdfErBoxTree(html);

        var imageChromium = await _helper.RasterizePdfAsync(pdfChromium);
        var imageBoxTree = await _helper.RasterizePdfAsync(pdfBoxTree);

        // Only assert both renderers produced a same-size page and some
        // non-trivial visual similarity -- not a strict SSIM threshold, since
        // this pipeline slice deliberately doesn't implement text color/font
        // rendering choices identically yet. This is a smoke test that the
        // pipeline produces plausible output, not a fidelity regression gate.
        Assert.True(Math.Abs(imageChromium.Width - imageBoxTree.Width) <= 10,
            $"Width mismatch: Chromium={imageChromium.Width}, BoxTree={imageBoxTree.Width}");
        Assert.True(Math.Abs(imageChromium.Height - imageBoxTree.Height) <= 10,
            $"Height mismatch: Chromium={imageChromium.Height}, BoxTree={imageBoxTree.Height}");

        var commonWidth = Math.Min(imageChromium.Width, imageBoxTree.Width);
        var commonHeight = Math.Min(imageChromium.Height, imageBoxTree.Height);
        imageChromium.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(0, 0, commonWidth, commonHeight)));
        imageBoxTree.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(0, 0, commonWidth, commonHeight)));

        var ssim = SsimCalculator.CalculateSSIM(imageChromium, imageBoxTree);
        Console.WriteLine($"Box-tree pipeline SSIM vs Chromium: {ssim:F4}");

        // Low bar deliberately: this pipeline slice has no text color, no font
        // matching guarantees, and no line-breaking, so it should merely be
        // "clearly a rendering of the same document," not near-perfect yet.
        Assert.True(ssim > 0.5, $"Expected some visual resemblance, got SSIM={ssim:F4}");
    }
}
