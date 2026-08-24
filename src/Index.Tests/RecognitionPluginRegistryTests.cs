using Index.Recognition;
using Index.Molecule;
using Index.Platform;

namespace Index.Tests;

public sealed class RecognitionPluginRegistryTests
{
    [Fact]
    public void MolGrapherRegistersAsMoleculeStructurePlugin()
    {
        using var molGrapher = new MolGrapherClient();
        using var registry = new RecognitionPluginRegistry(
            new IRecognitionPlugin[] { molGrapher });

        var selected = registry.GetRequired<MoleculeRecognitionResult>(
            RecognitionCapabilities.MoleculeStructure);

        Assert.Same(molGrapher, selected);
        Assert.Equal("molgrapher.local", selected.Descriptor.Id);
        Assert.Equal(typeof(MoleculeRecognitionResult), selected.OutputType);
    }

    [Fact]
    public void FindSelectsHighestPriorityCompatiblePlugin()
    {
        var lower = new FakePlugin<TextResult>("formula.local", 10);
        var higher = new FakePlugin<TextResult>("formula.fast", 50);
        var incompatible = new FakePlugin<NumberResult>("formula.number", 100);
        using var registry = new RecognitionPluginRegistry(
            new IRecognitionPlugin[] { lower, higher, incompatible });

        var selected = registry.Find<TextResult>(RecognitionCapabilities.ChemicalFormula);

        Assert.Same(higher, selected);
    }

    [Fact]
    public void FindHonorsPreferredPluginId()
    {
        var lower = new FakePlugin<TextResult>("formula.local", 10);
        var higher = new FakePlugin<TextResult>("formula.fast", 50);
        using var registry = new RecognitionPluginRegistry(
            new IRecognitionPlugin[] { lower, higher });

        var selected = registry.Find<TextResult>(
            RecognitionCapabilities.ChemicalFormula,
            "FORMULA.LOCAL");

        Assert.Same(lower, selected);
    }

    [Fact]
    public void ConstructorRejectsDuplicatePluginIds()
    {
        var first = new FakePlugin<TextResult>("duplicate", 10);
        var second = new FakePlugin<NumberResult>("DUPLICATE", 20);

        var error = Assert.Throws<ArgumentException>(() =>
            new RecognitionPluginRegistry(
                new IRecognitionPlugin[] { first, second }));

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetRequiredExplainsMissingCapability()
    {
        using var registry = new RecognitionPluginRegistry(
            Array.Empty<IRecognitionPlugin>());

        var error = Assert.Throws<InvalidOperationException>(() =>
            registry.GetRequired<TextResult>(RecognitionCapabilities.ChemicalFormula));

        Assert.Contains("chemical.formula", error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(TextResult).FullName!, error.Message, StringComparison.Ordinal);
    }

    private sealed record TextResult(string Text);

    private sealed record NumberResult(int Value);

    private sealed class FakePlugin<TOutput>(string id, int priority)
        : IRecognitionPlugin<TOutput>
        where TOutput : class
    {
        public RecognitionPluginDescriptor Descriptor { get; } = new(
            id,
            id,
            RecognitionCapabilities.ChemicalFormula,
            new Version(1, 0),
            priority);

        public Task<TOutput> RecognizeAsync(
            RecognitionInput input,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
