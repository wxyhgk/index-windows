using Index.Platform.Export;

namespace Index.Actions;

public sealed class SaveAction : ICaptureAction
{
    private static readonly IReadOnlySet<CaptureActionScope> SupportedScopes =
        new HashSet<CaptureActionScope>
        {
            CaptureActionScope.Capture,
            CaptureActionScope.Pinned
        };

    private readonly IImageExporter _exporter;

    public SaveAction(IImageExporter exporter)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
    }

    public CaptureActionDescriptor Descriptor { get; } = new(
        CaptureActionIds.Save,
        "保存",
        "✓",
        SupportedScopes);

    public async ValueTask PerformAsync(
        CaptureContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var png = await context.Artifact
            .GetRenderedPngAsync(cancellationToken)
            .ConfigureAwait(false);
        await _exporter.ExportPngAsync(
            png,
            context.SuggestedFileName,
            cancellationToken).ConfigureAwait(false);
    }
}
