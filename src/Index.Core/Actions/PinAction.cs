using Index.Pin;

namespace Index.Actions;

/// <summary>Turns the current non-destructive capture artifact into an independent pin window.</summary>
public sealed class PinAction : ICaptureAction
{
    private static readonly IReadOnlySet<CaptureActionScope> SupportedScopes =
        new HashSet<CaptureActionScope> { CaptureActionScope.Capture };

    private readonly IPinPresenter _presenter;

    public PinAction(IPinPresenter presenter)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
    }

    public CaptureActionDescriptor Descriptor { get; } = new(
        CaptureActionIds.Pin,
        "钉图",
        "📌",
        SupportedScopes);

    public async ValueTask PerformAsync(
        CaptureContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await _presenter.PresentAsync(context, cancellationToken).ConfigureAwait(false);
    }
}
