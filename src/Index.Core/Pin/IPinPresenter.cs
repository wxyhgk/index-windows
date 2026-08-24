using Index.Actions;

namespace Index.Pin;

/// <summary>Application boundary for presenting a capture as a pinned window.</summary>
public interface IPinPresenter
{
    ValueTask PresentAsync(
        CaptureContext context,
        CancellationToken cancellationToken = default);
}
