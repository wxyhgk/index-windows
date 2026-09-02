using System.Collections.Frozen;

namespace Index.Storage;

public enum LibraryWorkspaceContent
{
    Gallery,
    Collections
}

public enum LibraryWorkspaceStatus
{
    Idle,
    Loading,
    Gallery,
    Collections,
    Error,
    Cancelled
}

public sealed record GalleryHomeSnapshot(long TotalCount, ShotPage FirstPage);

public sealed record CollectionsHomeSnapshot(
    IReadOnlySet<long> FavoriteShotIds,
    IReadOnlyList<ShotCollectionRecord> Collections);

public sealed record LibraryWorkspaceState(
    long Generation,
    LibraryWorkspaceContent Content,
    LibraryWorkspaceStatus Status,
    GalleryHomeSnapshot? Gallery = null,
    CollectionsHomeSnapshot? Collections = null,
    string? Error = null);

/// <summary>
/// Owns the cancellable query lifecycle for the main gallery and collections pages.
/// The WinUI shell only renders immutable snapshots returned by this controller.
/// </summary>
public sealed class LibraryWorkspaceController : IDisposable
{
    private readonly object _gate = new();
    private readonly IShotGallerySource _gallery;
    private readonly ILibraryOrganizationSource _organization;
    private CancellationTokenSource? _currentCancellation;
    private LibraryWorkspaceState _state = new(
        0,
        LibraryWorkspaceContent.Gallery,
        LibraryWorkspaceStatus.Idle);
    private long _generation;
    private bool _disposed;

    public LibraryWorkspaceController(
        IShotGallerySource gallery,
        ILibraryOrganizationSource organization)
    {
        _gallery = gallery ?? throw new ArgumentNullException(nameof(gallery));
        _organization = organization ?? throw new ArgumentNullException(nameof(organization));
    }

    public LibraryWorkspaceState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public Task<LibraryWorkspaceState> LoadGalleryAsync(
        CancellationToken cancellationToken = default) =>
        RunAsync(
            LibraryWorkspaceContent.Gallery,
            async token =>
            {
                var countTask = _gallery.GetCountAsync(token);
                var pageTask = _gallery.GetPageAsync(cancellationToken: token);
                await Task.WhenAll(countTask, pageTask).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var page = await pageTask.ConfigureAwait(false);
                var snapshot = new GalleryHomeSnapshot(
                    await countTask.ConfigureAwait(false),
                    new ShotPage(
                        Array.AsReadOnly(page.Items.ToArray()),
                        page.NextCursor));
                return new LibraryWorkspaceState(
                    0,
                    LibraryWorkspaceContent.Gallery,
                    LibraryWorkspaceStatus.Gallery,
                    Gallery: snapshot);
            },
            cancellationToken);

    public Task<LibraryWorkspaceState> LoadCollectionsAsync(
        CancellationToken cancellationToken = default) =>
        RunAsync(
            LibraryWorkspaceContent.Collections,
            async token =>
            {
                var favoritesTask = _organization.GetFavoriteIdsAsync(token);
                var collectionsTask = _organization.GetCollectionsAsync(token);
                await Task.WhenAll(favoritesTask, collectionsTask).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var snapshot = new CollectionsHomeSnapshot(
                    (await favoritesTask.ConfigureAwait(false)).ToFrozenSet(),
                    Array.AsReadOnly((await collectionsTask.ConfigureAwait(false)).ToArray()));
                return new LibraryWorkspaceState(
                    0,
                    LibraryWorkspaceContent.Collections,
                    LibraryWorkspaceStatus.Collections,
                    Collections: snapshot);
            },
            cancellationToken);

    public void CancelCurrent()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed)
                return;
            cancellation = _currentCancellation;
            _currentCancellation = null;
            _state = new LibraryWorkspaceState(
                ++_generation,
                _state.Content,
                LibraryWorkspaceStatus.Cancelled);
        }

        Cancel(cancellation);
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            cancellation = _currentCancellation;
            _currentCancellation = null;
            ++_generation;
        }

        Cancel(cancellation);
    }

    private async Task<LibraryWorkspaceState> RunAsync(
        LibraryWorkspaceContent content,
        Func<CancellationToken, Task<LibraryWorkspaceState>> query,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource linkedCancellation;
        CancellationTokenSource? previousCancellation;
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previousCancellation = _currentCancellation;
            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _currentCancellation = linkedCancellation;
            generation = ++_generation;
            _state = new LibraryWorkspaceState(
                generation,
                content,
                LibraryWorkspaceStatus.Loading);
        }

        Cancel(previousCancellation);
        var token = linkedCancellation.Token;
        try
        {
            var result = await query(token).ConfigureAwait(false);
            return CompleteIfCurrent(result with { Generation = generation }, generation);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return CompleteIfCurrent(
                new LibraryWorkspaceState(
                    generation,
                    content,
                    LibraryWorkspaceStatus.Cancelled),
                generation);
        }
        catch (Exception error)
        {
            return CompleteIfCurrent(
                new LibraryWorkspaceState(
                    generation,
                    content,
                    LibraryWorkspaceStatus.Error,
                    Error: error.Message),
                generation);
        }
        finally
        {
            lock (_gate)
            {
                if (generation == _generation
                    && ReferenceEquals(_currentCancellation, linkedCancellation))
                {
                    _currentCancellation = null;
                }
            }

            linkedCancellation.Dispose();
        }
    }

    private LibraryWorkspaceState CompleteIfCurrent(
        LibraryWorkspaceState completed,
        long generation)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation)
            {
                return new LibraryWorkspaceState(
                    generation,
                    completed.Content,
                    LibraryWorkspaceStatus.Cancelled);
            }

            _state = completed;
            return completed;
        }
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
