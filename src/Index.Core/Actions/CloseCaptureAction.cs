namespace Index.Actions;

/// <summary>Closes the capture host that supplied the action context.</summary>
public sealed class CloseCaptureAction : ICaptureAction
{
    private static readonly IReadOnlySet<CaptureActionScope> SupportedScopes =
        new HashSet<CaptureActionScope> { CaptureActionScope.Pinned };

    public CaptureActionDescriptor Descriptor { get; } = new(
        CaptureActionIds.Close,
        "关闭",
        "×",
        SupportedScopes);

    public ValueTask PerformAsync(
        CaptureContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        context.Host?.Dismiss();
        return ValueTask.CompletedTask;
    }
}
