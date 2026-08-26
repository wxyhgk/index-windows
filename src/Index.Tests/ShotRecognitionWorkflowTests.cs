using Index.Molecule;
using Index.Recognition;
using Index.Storage;

namespace Index.Tests;

public sealed class ShotRecognitionWorkflowTests
{
    [Fact]
    public async Task RunTransitionsFromPendingToSuccess()
    {
        var completion = new TaskCompletionSource<TextOutput>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var plugin = new FakePlugin((_, _) => completion.Task);
        using var registry = new RecognitionPluginRegistry([plugin]);
        using var workflow = MakeWorkflow(
            new FakeAssets(ShotAssetStatus.Original, [1, 2, 3]),
            registry);

        var runTask = workflow.RunAsync(MakeShot("png"));

        Assert.Equal(RecognitionWorkflowStatus.Pending, workflow.Status);
        completion.SetResult(new TextOutput("H2O"));
        var result = await runTask;
        Assert.Equal(RecognitionWorkflowStatus.Success, result.Status);
        Assert.Equal(RecognitionWorkflowStatus.Success, workflow.Status);
        Assert.Equal("H2O", result.Output?.Text);
        Assert.Equal("image/png", plugin.LastInput?.MediaType);
    }

    [Fact]
    public async Task ThumbnailFallbackProducesWarningAndJpegMediaType()
    {
        var plugin = new FakePlugin((_, _) =>
            Task.FromResult(new TextOutput("CO2")));
        using var registry = new RecognitionPluginRegistry([plugin]);
        using var workflow = MakeWorkflow(
            new FakeAssets(
                ShotAssetStatus.ThumbnailFallback,
                [4, 5],
                "Original missing"),
            registry);

        var result = await workflow.RunAsync(MakeShot("png"));

        Assert.Equal(RecognitionWorkflowStatus.Success, result.Status);
        Assert.Equal("Original missing", result.Warning);
        Assert.Equal("image/jpeg", plugin.LastInput?.MediaType);
    }

    [Fact]
    public async Task MissingAssetReturnsErrorWithoutCallingPlugin()
    {
        var plugin = new FakePlugin((_, _) =>
            Task.FromResult(new TextOutput("unused")));
        using var registry = new RecognitionPluginRegistry([plugin]);
        using var workflow = MakeWorkflow(
            new FakeAssets(ShotAssetStatus.Missing, [], "Image missing"),
            registry);

        var result = await workflow.RunAsync(MakeShot("png"));

        Assert.Equal(RecognitionWorkflowStatus.Error, result.Status);
        Assert.Equal("Image missing", result.Error);
        Assert.Equal(0, plugin.CallCount);
    }

    [Fact]
    public async Task NewRunCancelsPreviousRunWithoutOverwritingCurrentStatus()
    {
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var plugin = new FakePlugin(async (_, token) =>
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                firstStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return new TextOutput("second");
        });
        using var registry = new RecognitionPluginRegistry([plugin]);
        using var workflow = MakeWorkflow(
            new FakeAssets(ShotAssetStatus.Original, [1]),
            registry);

        var first = workflow.RunAsync(MakeShot("png"));
        await firstStarted.Task;
        var second = workflow.RunAsync(MakeShot("png"));

        var secondResult = await second;
        var firstResult = await first;
        Assert.Equal(RecognitionWorkflowStatus.Success, secondResult.Status);
        Assert.Equal(RecognitionWorkflowStatus.Cancelled, firstResult.Status);
        Assert.Equal(RecognitionWorkflowStatus.Success, workflow.Status);
    }

    [Theory]
    [InlineData("mol", null, null, RecognitionWorkflowStatus.Success)]
    [InlineData(null, "CC", null, RecognitionWorkflowStatus.Success)]
    [InlineData(null, null, null, RecognitionWorkflowStatus.Empty)]
    [InlineData(null, null, "model failed", RecognitionWorkflowStatus.Error)]
    public void MoleculePolicyClassifiesDomainResult(
        string? sdf,
        string? smiles,
        string? error,
        RecognitionWorkflowStatus expected)
    {
        var result = new MoleculeRecognitionResult(smiles, 0.9, sdf, 10, error);

        var evaluation = new MoleculeRecognitionOutputPolicy().Evaluate(result);

        Assert.Equal(expected, evaluation.Status);
        Assert.Equal(error, evaluation.Error);
    }

    private static ShotRecognitionWorkflow<TextOutput> MakeWorkflow(
        IShotAssetReader assets,
        IRecognitionPluginRegistry registry) =>
        new(
            assets,
            registry,
            RecognitionCapabilities.ChemicalFormula,
            new TextOutputPolicy());

    private static ShotRecord MakeShot(string extension) => new(
        Id: 1,
        Sha256: "workflow",
        CapturedAt: DateTimeOffset.UtcNow,
        PixelWidth: 100,
        PixelHeight: 100,
        Scale: 1,
        AppName: null,
        AppIdentifier: null,
        WindowTitle: null,
        SourceUrl: null,
        DisplayIndex: null,
        DisplayName: null,
        RegionX: 0,
        RegionY: 0,
        RegionWidth: 100,
        RegionHeight: 100,
        OriginalExtension: extension);

    private sealed record TextOutput(string Text);

    private sealed class TextOutputPolicy : IRecognitionOutputPolicy<TextOutput>
    {
        public RecognitionOutputEvaluation Evaluate(TextOutput output) =>
            string.IsNullOrWhiteSpace(output.Text)
                ? RecognitionOutputEvaluation.Empty()
                : RecognitionOutputEvaluation.Success();
    }

    private sealed class FakeAssets(
        ShotAssetStatus status,
        byte[] data,
        string? warning = null) : IShotAssetReader
    {
        public Task<ShotAssetReadResult> ReadBestAvailableAsync(
            ShotRecord shot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ShotAssetReadResult(status, data, warning));
    }

    private sealed class FakePlugin(
        Func<RecognitionInput, CancellationToken, Task<TextOutput>> recognize)
        : IRecognitionPlugin<TextOutput>
    {
        public RecognitionPluginDescriptor Descriptor { get; } = new(
            "formula.fake",
            "Formula Fake",
            RecognitionCapabilities.ChemicalFormula,
            new Version(1, 0));

        public RecognitionInput? LastInput { get; private set; }

        public int CallCount { get; private set; }

        public Task<TextOutput> RecognizeAsync(
            RecognitionInput input,
            CancellationToken cancellationToken = default)
        {
            LastInput = input;
            CallCount++;
            return recognize(input, cancellationToken);
        }
    }
}
