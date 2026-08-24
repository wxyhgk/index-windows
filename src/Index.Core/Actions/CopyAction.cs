using Index.Platform.Clipboard;

namespace Index.Actions;

/// <summary>将底图与当前标注合成后的最终 PNG 写入系统剪贴板。</summary>
public sealed class CopyAction : ICaptureAction
{
    private static readonly IReadOnlySet<CaptureActionScope> SupportedScopes =
        new HashSet<CaptureActionScope>
        {
            CaptureActionScope.Capture,
            CaptureActionScope.Pinned
        };

    private readonly IClipboardWriter _clipboard;

    public CopyAction(IClipboardWriter clipboard)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
    }

    public CaptureActionDescriptor Descriptor { get; } = new(
        CaptureActionIds.Copy,
        "复制",
        "⧉",
        SupportedScopes);

    public bool SuppressesAutoCopy => true;

    public async ValueTask PerformAsync(
        CaptureContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var renderedPng = await context.Artifact
            .GetRenderedPngAsync(cancellationToken)
            .ConfigureAwait(false);
        await _clipboard
            .WritePngAsync(renderedPng, cancellationToken)
            .ConfigureAwait(false);
    }
}
