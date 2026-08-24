namespace Index.Recognition;

public readonly record struct RecognitionCapability
{
    public RecognitionCapability(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Recognition capability cannot be empty.", nameof(value));
        }

        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public static class RecognitionCapabilities
{
    public static readonly RecognitionCapability MoleculeStructure = new("molecule.structure");

    public static readonly RecognitionCapability ChemicalFormula = new("chemical.formula");
}

public enum RecognitionInputKind
{
    Image
}

public sealed record RecognitionInput(
    RecognitionInputKind Kind,
    ReadOnlyMemory<byte> Content,
    string? MediaType = null)
{
    public static RecognitionInput Image(
        ReadOnlyMemory<byte> content,
        string? mediaType = null) =>
        new(RecognitionInputKind.Image, content, mediaType);
}

public sealed record RecognitionPluginDescriptor(
    string Id,
    string DisplayName,
    RecognitionCapability Capability,
    Version Version,
    int Priority = 0);

public interface IRecognitionPlugin
{
    RecognitionPluginDescriptor Descriptor { get; }

    Type OutputType { get; }
}

public interface IRecognitionPlugin<TOutput> : IRecognitionPlugin
{
    Type IRecognitionPlugin.OutputType => typeof(TOutput);

    Task<TOutput> RecognizeAsync(
        RecognitionInput input,
        CancellationToken cancellationToken = default);
}
