using Index.Storage;

namespace Index.Editor;

public sealed record EditorFilmstripState(
    IReadOnlyList<ShotRecord> Items,
    long CurrentShotId,
    int CurrentIndex,
    bool HasOlderItems,
    bool IsLoading,
    string? Error)
{
    public ShotRecord? Current => CurrentIndex >= 0 && CurrentIndex < Items.Count
        ? Items[CurrentIndex]
        : null;
}

/// <summary>
/// Owns the ordered shot sequence shown by the editor filmstrip. It only resolves
/// selection candidates; the editor host remains responsible for flushing the old
/// session before committing a new current shot with <see cref="SetCurrentShot"/>.
/// </summary>
public sealed class EditorFilmstripController : IDisposable
{
    public const int DefaultPageSize = 100;

    private readonly object _gate = new();
    private readonly IShotPageSource _source;
    private readonly int _pageSize;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private List<ShotRecord> _items;
    private ShotRecord _current;
    private ShotPageCursor? _nextCursor;
    private CancellationTokenSource? _operationCancellation;
    private long _generation;
    private bool _isLoading;
    private string? _error;
    private bool _disposed;

    public EditorFilmstripController(
        IShotPageSource source,
        ShotRecord current,
        int pageSize = DefaultPageSize)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        _pageSize = pageSize;
        _items = [current];
    }

    public event Action<EditorFilmstripState>? StateChanged;

    public EditorFilmstripState State
    {
        get
        {
            lock (_gate)
                return SnapshotLocked();
        }
    }

    /// <summary>Reloads from the beginning until the stable current shot is found.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var operation = BeginOperation(cancellationToken);
        try
        {
            var loaded = new List<ShotRecord>();
            var ids = new HashSet<long>();
            ShotPageCursor? cursor = null;
            ShotPageCursor? nextCursor;
            var foundCurrent = false;
            var seenCursors = new HashSet<ShotPageCursor>();

            do
            {
                operation.Token.ThrowIfCancellationRequested();
                var page = await _source.GetPageAsync(
                    _pageSize,
                    cursor,
                    operation.Token).ConfigureAwait(false);
                foreach (var shot in page.Items)
                {
                    if (ids.Add(shot.Id))
                        loaded.Add(shot);
                    foundCurrent |= shot.Id == operation.CurrentShotId;
                }

                nextCursor = page.NextCursor;
                if (foundCurrent || nextCursor is null || !seenCursors.Add(nextCursor.Value))
                    break;
                cursor = nextCursor;
            }
            while (true);

            PublishReload(operation, loaded, nextCursor);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            PublishError(operation, error.Message);
        }
        finally
        {
            EndOperation(operation);
        }
    }

    /// <summary>
    /// Resolves the adjacent shot without changing CurrentShotId. A host should flush
    /// its active editor, open the returned shot, then call SetCurrentShot.
    /// </summary>
    public async Task<ShotRecord?> GetAdjacentAsync(
        int direction,
        CancellationToken cancellationToken = default)
    {
        if (direction == 0)
            return null;
        direction = Math.Sign(direction);

        lock (_gate)
        {
            ThrowIfDisposedLocked();
            var currentIndex = _items.FindIndex(item => item.Id == _current.Id);
            var targetIndex = currentIndex + direction;
            if (currentIndex >= 0 && targetIndex >= 0 && targetIndex < _items.Count)
                return _items[targetIndex];
            if (direction < 0 || _nextCursor is null)
                return null;
        }

        var operation = BeginOperation(cancellationToken);
        try
        {
            ShotPageCursor cursor;
            lock (_gate)
            {
                if (_nextCursor is not { } available)
                    return null;
                cursor = available;
            }

            var page = await _source.GetPageAsync(
                _pageSize,
                cursor,
                operation.Token).ConfigureAwait(false);
            return PublishAppendAndResolve(operation, page, direction);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error)
        {
            PublishError(operation, error.Message);
            return null;
        }
        finally
        {
            EndOperation(operation);
        }
    }

    public void SetCurrentShot(ShotRecord shot)
    {
        ArgumentNullException.ThrowIfNull(shot);
        EditorFilmstripState state;
        lock (_gate)
        {
            ThrowIfDisposedLocked();
            CancelOperationLocked();
            _current = shot;
            var index = _items.FindIndex(item => item.Id == shot.Id);
            if (index < 0)
                _items.Add(shot);
            else
                _items[index] = shot;
            _error = null;
            state = SnapshotLocked();
        }
        NotifyStateChanged(state);
    }

    private Operation BeginOperation(CancellationToken cancellationToken)
    {
        EditorFilmstripState state;
        Operation operation;
        lock (_gate)
        {
            ThrowIfDisposedLocked();
            CancelOperationLocked();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token);
            _operationCancellation = cancellation;
            _isLoading = true;
            _error = null;
            operation = new Operation(++_generation, _current.Id, cancellation);
            state = SnapshotLocked();
        }
        NotifyStateChanged(state);
        return operation;
    }

    private void PublishReload(
        Operation operation,
        List<ShotRecord> loaded,
        ShotPageCursor? nextCursor)
    {
        EditorFilmstripState? state = null;
        lock (_gate)
        {
            if (!IsCurrentLocked(operation))
                return;
            if (loaded.All(item => item.Id != _current.Id))
                loaded.Add(_current);
            _items = loaded;
            _nextCursor = nextCursor;
            state = SnapshotLocked();
        }
        NotifyStateChanged(state);
    }

    private ShotRecord? PublishAppendAndResolve(
        Operation operation,
        ShotPage page,
        int direction)
    {
        EditorFilmstripState? state = null;
        ShotRecord? target = null;
        lock (_gate)
        {
            if (!IsCurrentLocked(operation))
                return null;
            var ids = _items.Select(item => item.Id).ToHashSet();
            foreach (var shot in page.Items)
            {
                if (ids.Add(shot.Id))
                    _items.Add(shot);
            }
            _nextCursor = page.NextCursor;
            var currentIndex = _items.FindIndex(item => item.Id == operation.CurrentShotId);
            var targetIndex = currentIndex + direction;
            if (currentIndex >= 0 && targetIndex >= 0 && targetIndex < _items.Count)
                target = _items[targetIndex];
            state = SnapshotLocked();
        }
        NotifyStateChanged(state);
        return target;
    }

    private void PublishError(Operation operation, string message)
    {
        EditorFilmstripState? state = null;
        lock (_gate)
        {
            if (!IsCurrentLocked(operation))
                return;
            _error = message;
            state = SnapshotLocked();
        }
        NotifyStateChanged(state);
    }

    private void EndOperation(Operation operation)
    {
        EditorFilmstripState? state = null;
        lock (_gate)
        {
            if (IsCurrentLocked(operation))
            {
                _operationCancellation = null;
                _isLoading = false;
                state = SnapshotLocked();
            }
        }
        operation.Cancellation.Dispose();
        if (state is not null)
            NotifyStateChanged(state);
    }

    private bool IsCurrentLocked(Operation operation)
        => !_disposed
           && operation.Generation == _generation
           && ReferenceEquals(_operationCancellation, operation.Cancellation)
           && !operation.Token.IsCancellationRequested;

    private void CancelOperationLocked()
    {
        _generation++;
        _operationCancellation?.Cancel();
        _operationCancellation = null;
        _isLoading = false;
    }

    private EditorFilmstripState SnapshotLocked()
    {
        var snapshot = _items.ToArray();
        var currentIndex = Array.FindIndex(snapshot, item => item.Id == _current.Id);
        return new EditorFilmstripState(
            snapshot,
            _current.Id,
            currentIndex,
            _nextCursor is not null,
            _isLoading,
            _error);
    }

    private void NotifyStateChanged(EditorFilmstripState state)
    {
        if (StateChanged is not { } handlers)
            return;
        foreach (Action<EditorFilmstripState> handler in handlers.GetInvocationList())
        {
            try { handler(state); }
            catch { }
        }
    }

    private void ThrowIfDisposedLocked()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            CancelOperationLocked();
            _lifetimeCancellation.Cancel();
        }
        _lifetimeCancellation.Dispose();
    }

    private sealed record Operation(
        long Generation,
        long CurrentShotId,
        CancellationTokenSource Cancellation)
    {
        public CancellationToken Token => Cancellation.Token;
    }
}

public readonly record struct EditorThumbnailLayout(
    double X,
    double Y,
    double Width,
    double Height);

public static class EditorThumbnailFit
{
    public static EditorThumbnailLayout Calculate(
        double sourceWidth,
        double sourceHeight,
        double boundsWidth,
        double boundsHeight,
        double padding = 0)
    {
        RequirePositiveFinite(sourceWidth, nameof(sourceWidth));
        RequirePositiveFinite(sourceHeight, nameof(sourceHeight));
        RequirePositiveFinite(boundsWidth, nameof(boundsWidth));
        RequirePositiveFinite(boundsHeight, nameof(boundsHeight));
        if (!double.IsFinite(padding) || padding < 0)
            throw new ArgumentOutOfRangeException(nameof(padding));

        var availableWidth = Math.Max(0, boundsWidth - (padding * 2));
        var availableHeight = Math.Max(0, boundsHeight - (padding * 2));
        if (availableWidth == 0 || availableHeight == 0)
            return new EditorThumbnailLayout(boundsWidth / 2, boundsHeight / 2, 0, 0);

        var scale = Math.Min(
            availableWidth / sourceWidth,
            availableHeight / sourceHeight);
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        return new EditorThumbnailLayout(
            (boundsWidth - width) / 2,
            (boundsHeight - height) / 2,
            width,
            height);
    }

    private static void RequirePositiveFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}
