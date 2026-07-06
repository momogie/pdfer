namespace PdfEr.FidelityTests;

public class FidelityTests : IAsyncLifetime
{
    private PdfRenderingHelper? _helper;
    private string _outputDir = null!;

    public async Task InitializeAsync()
    {
        _helper = await PdfRenderingHelper.CreateAsync();
        _outputDir = Path.Combine(Path.GetTempPath(), $"PdfEr_Fidelity_Results_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputDir);
    }

    public async Task DisposeAsync()
    {
        if (_helper != null)
            await _helper.DisposeAsync();

        // Clean up output if tests pass (optionally keep on failure for debugging)
        // Directory.Delete(_outputDir, true);
    }

    [Fact]
    public async Task RunAllFidelityTests()
    {
        var casesDir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(FidelityTests).Assembly.Location) ?? "",
            "..", "..", "..", "fidelity", "cases"
        ));

        Assert.True(Directory.Exists(casesDir), $"Cases directory not found: {casesDir}");

        var htmlFiles = Directory.GetFiles(casesDir, "*.html");
        Assert.True(htmlFiles.Length > 0, $"No HTML test cases found in {casesDir}");

        var runner = new FidelityTestRunner(_helper!, casesDir, _outputDir);
        await runner.RunAllTestsAsync();

        Console.WriteLine($"Results saved to: {_outputDir}");
    }
}
