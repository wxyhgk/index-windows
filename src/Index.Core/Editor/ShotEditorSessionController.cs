using Index.Annotation;
using Index.Storage;

namespace Index.Editor;

public enum ShotEditorLoadStatus
{
    Ready,
    Degraded,
    Missing,
    Corrupt
}

public sealed record ShotEditorLoadResult(
    ShotEditorLoadStatus Status,
    ReadOnlyMemory<byte> BasePng,
    string? Warning)
{
    public bool CanEdit => Status == ShotEditorLoadStatus.Ready && !BasePng.IsEmpty;
}

/// <summary>
/// Immutable metadata for one entry in the editor's revision picker. Layer snapshots
/// remain private to the session so consumers cannot mutate saved history in place.
/// </summary>
public sealed record ShotEditorHistoryItem(
    long RevisionId,
    long? ParentRevisionId,
    DateTimeOffset CreatedAt,
    string? Note,
    int LayerCount);

/// <summary>
/// Owns one gallery editing session. The original pixels remain immutable; edits are
/// debounced into append-only complete revision snapshots and flushed before navigation.
/// </summary>
public sealed class ShotEditorSessionController : IAsyncDisposable
{
    private static readonly TimeSpan DefaultAutoSaveDelay = TimeSpan.FromMilliseconds(1200);

    private readonly IShotAssetReader _assets;
    private readonly IShotEditorRepository _repository;
    private readonly TimeSpan _autoSaveDelay;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private CancellationTokenSource? _debounceCancellation;
    private Func<Func<Task>, CancellationToken, Task>? _sessionDispatcher;
    private Layers<ImageSpace> _baseline = new();
    private Dictionary<long, ShotRevisionSnapshot> _revisionSnapshots = [];
    private IReadOnlyList<ShotEditorHistoryItem> _revisionHistory =
        Array.AsReadOnly(Array.Empty<ShotEditorHistoryItem>());
    private long _changeVersion;
    private long _revisionSelectionGeneration;
    private int _loadStarted;
    private bool _replacingRevision;
    private bool _loaded;
    private bool _canEdit;
    private bool _disposed;

    public ShotEditorSessionController(
        ShotRecord shot,
        IShotAssetReader assets,
        IShotEditorRepository repository,
        TimeSpan? autoSaveDelay = null)
    {
        Shot = shot ?? throw new ArgumentNullException(nameof(shot));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _autoSaveDelay = autoSaveDelay ?? DefaultAutoSaveDelay;
        if (_autoSaveDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(autoSaveDelay));
    }

    public ShotRecord Shot { get; }
    public AnnotationState Annotation { get; } = new();
    public ReadOnlyMemory<byte> BasePng { get; private set; }
    public DateTimeOffset? SavedAt { get; private set; }
    public IReadOnlyList<ShotEditorHistoryItem> RevisionHistory => _revisionHistory;
    public long? CurrentRevisionId { get; private set; }
    public bool HasPendingChanges => _canEdit && !LayersEqual(CurrentLayers(), _baseline);

    public event Action? StateChanged;
    public event Action<Exception>? SaveFailed;

    /// <summary>
    /// Binds mutations of the shared annotation state to its owning thread. UI hosts
    /// must configure this before loading because WinUI does not guarantee an ambient
    /// SynchronizationContext for async event continuations.
    /// </summary>
    public void SetSessionDispatcher(
        Func<Func<Task>, CancellationToken, Task> dispatcher)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (_loaded)
            throw new InvalidOperationException("The session dispatcher must be set before loading.");
        _sessionDispatcher = dispatcher;
    }

    public async Task<ShotEditorLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref _loadStarted, 1, 0) != 0)
            throw new InvalidOperationException("The editor session is already loaded.");
        long generation = Interlocked.Increment(ref _revisionSelectionGeneration);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        var assetTask = _assets.ReadBestAvailableAsync(Shot, linked.Token);
        var historyTask = _repository.GetRevisionHistoryAsync(Shot.Id, linked.Token);
        await Task.WhenAll(assetTask, historyTask).ConfigureAwait(false);

        var asset = await assetTask.ConfigureAwait(false);
        if (!asset.HasData)
        {
            return new ShotEditorLoadResult(
                asset.Status == ShotAssetStatus.Corrupt
                    ? ShotEditorLoadStatus.Corrupt
                    : ShotEditorLoadStatus.Missing,
                ReadOnlyMemory<byte>.Empty,
                asset.Warning);
        }

        var history = (await historyTask.ConfigureAwait(false))
            .OrderBy(revision => revision.RevisionId)
            .ToArray();
        var latest = history.LastOrDefault();
        await RunInSessionContextAsync(() =>
        {
            linked.Token.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _revisionSelectionGeneration))
                return Task.CompletedTask;
            BasePng = asset.Data;
            var layers = latest?.Layers ?? new Layers<ImageSpace>();
            Annotation.LoadImageLayers(layers);
            _baseline = Clone(layers);
            ReplaceRevisionHistory(history);
            CurrentRevisionId = latest?.RevisionId;
            _loaded = true;
            _canEdit = asset.Status == ShotAssetStatus.Original;
            Annotation.Changed += OnAnnotationChanged;
            NotifyStateChanged();
            return Task.CompletedTask;
        }, linked.Token).ConfigureAwait(false);

        if (!_canEdit)
        {
            return new ShotEditorLoadResult(
                ShotEditorLoadStatus.Degraded,
                BasePng,
                asset.Warning ?? "原图不可用，当前只显示缩略图，已禁用编辑保存。 ");
        }

        return new ShotEditorLoadResult(ShotEditorLoadStatus.Ready, BasePng, asset.Warning);
    }

    public Layers<ImageSpace> CurrentLayers()
        => Annotation.ExportLayers(
            new LRect(0, 0, Shot.PixelWidth, Shot.PixelHeight),
            1);

    /// <summary>
    /// Loads a saved complete revision into the editable canvas. Unsaved canvas changes
    /// are intentionally discarded; choosing history is a restore operation. The selected
    /// revision becomes the content-comparison baseline but is never rewritten.
    /// </summary>
    public async Task<bool> SelectRevisionAsync(
        long revisionId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_loaded)
            throw new InvalidOperationException("The editor session has not been loaded.");

        CancelDebounce();
        long generation = Interlocked.Increment(ref _revisionSelectionGeneration);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        await _flushGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            bool selected = false;
            await RunInSessionContextAsync(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                if (generation != Volatile.Read(ref _revisionSelectionGeneration))
                    return Task.CompletedTask;
                if (!_revisionSnapshots.TryGetValue(revisionId, out var revision))
                    throw new KeyNotFoundException(
                        $"Revision {revisionId} does not belong to shot {Shot.Id}.");

                var layers = revision.Layers;
                bool alreadySelected = CurrentRevisionId == revisionId
                    && LayersEqual(CurrentLayers(), _baseline);
                if (alreadySelected)
                    return Task.CompletedTask;

                CancelDebounce();
                _replacingRevision = true;
                try
                {
                    Annotation.LoadImageLayers(layers);
                    _baseline = Clone(layers);
                    CurrentRevisionId = revisionId;
                    Interlocked.Increment(ref _changeVersion);
                }
                finally
                {
                    _replacingRevision = false;
                }
                selected = true;
                NotifyStateChanged();
                return Task.CompletedTask;
            }, linked.Token).ConfigureAwait(false);
            return selected;
        }
        finally
        {
            _flushGate.Release();
        }
    }

    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CancelDebounce();
        return await FlushCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> FlushCoreAsync(CancellationToken cancellationToken)
    {
        if (!_canEdit)
            return false;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        await _flushGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            Layers<ImageSpace>? snapshot = null;
            long snapshotVersion = 0;
            // Capture only after entering the gate. An older autosave can never enqueue a
            // stale snapshot behind a newer explicit flush and append history backwards.
            await RunInSessionContextAsync(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                _replacingRevision = true;
                try
                {
                    Annotation.EndTextEditing();
                    snapshot = CurrentLayers();
                }
                finally
                {
                    _replacingRevision = false;
                }
                snapshotVersion = Volatile.Read(ref _changeVersion);
                return Task.CompletedTask;
            }, linked.Token).ConfigureAwait(false);
            if (snapshot is null)
                return false;
            if (LayersEqual(snapshot, _baseline))
                return false;

            var savedRevision = await Task.Run(
                () => _repository.AppendRevisionAsync(
                    Shot.Id,
                    snapshot,
                    $"编辑 {snapshot.Count} 层",
                    linked.Token),
                linked.Token).ConfigureAwait(false);
            // Persistence has completed at this point. Keep this bookkeeping independent
            // from the UI dispatcher so a synchronous final flush cannot deadlock a host
            // that is already waiting on the UI thread.
            _baseline = Clone(snapshot);
            SavedAt = savedRevision.CreatedAt;
            CurrentRevisionId = savedRevision.Id;
            AddSavedRevision(savedRevision, snapshot);
            NotifyStateChanged();

            if (Volatile.Read(ref _changeVersion) != snapshotVersion)
                ScheduleAutoSave();
            return true;
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private void OnAnnotationChanged()
    {
        if (_replacingRevision)
            return;
        Interlocked.Increment(ref _changeVersion);
        NotifyStateChanged();
        ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        if (!_canEdit || _disposed)
            return;

        CancelDebounce();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        _debounceCancellation = cancellation;
        _ = RunAutoSaveAsync(cancellation);
    }

    private async Task RunAutoSaveAsync(CancellationTokenSource owner)
    {
        try
        {
            await Task.Delay(_autoSaveDelay, owner.Token).ConfigureAwait(false);
            await FlushCoreAsync(owner.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            NotifySaveFailed(error);
        }
        finally
        {
            Interlocked.CompareExchange(ref _debounceCancellation, null, owner);
            owner.Dispose();
        }
    }

    private Task RunInSessionContextAsync(
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        return _sessionDispatcher is null
            ? operation()
            : _sessionDispatcher(operation, cancellationToken);
    }

    private void CancelDebounce()
    {
        var cancellation = Interlocked.Exchange(ref _debounceCancellation, null);
        if (cancellation is null)
            return;
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void NotifyStateChanged()
    {
        if (StateChanged is not { } handlers)
            return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch { }
        }
    }

    private void NotifySaveFailed(Exception error)
    {
        if (SaveFailed is not { } handlers)
            return;
        foreach (Action<Exception> handler in handlers.GetInvocationList())
        {
            try { handler(error); }
            catch { }
        }
    }

    private void ReplaceRevisionHistory(IEnumerable<ShotRevisionSnapshot> revisions)
    {
        var snapshots = new Dictionary<long, ShotRevisionSnapshot>();
        foreach (var revision in revisions.OrderBy(item => item.RevisionId))
        {
            if (!snapshots.TryAdd(
                    revision.RevisionId,
                    revision with { Layers = Clone(revision.Layers) }))
            {
                throw new InvalidDataException(
                    $"Shot {Shot.Id} contains duplicate revision {revision.RevisionId}.");
            }
        }

        _revisionSnapshots = snapshots;
        _revisionHistory = Array.AsReadOnly(
            snapshots.Values
                .OrderBy(item => item.RevisionId)
                .Select(item => new ShotEditorHistoryItem(
                    item.RevisionId,
                    item.ParentRevisionId,
                    item.CreatedAt,
                    item.Note,
                    item.Layers.Count))
                .ToArray());
    }

    private void AddSavedRevision(
        RevisionRecord revision,
        Layers<ImageSpace> layers)
    {
        var snapshot = new ShotRevisionSnapshot(revision.Id, Clone(layers))
        {
            ParentRevisionId = revision.ParentId,
            CreatedAt = revision.CreatedAt,
            Note = revision.Note
        };
        ReplaceRevisionHistory(
            _revisionSnapshots.Values
                .Where(item => item.RevisionId != revision.Id)
                .Append(snapshot));
    }

    private static Layers<ImageSpace> Clone(Layers<ImageSpace> source)
    {
        var clone = new Layers<ImageSpace>();
        foreach (var layer in source.Elements)
            clone.Append(layer with { });
        return clone;
    }

    private static bool LayersEqual(Layers<ImageSpace> left, Layers<ImageSpace> right)
        => left.Elements.SequenceEqual(right.Elements);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        Layers<ImageSpace>? finalSnapshot = null;
        if (_loaded && _canEdit)
        {
            _replacingRevision = true;
            try
            {
                Annotation.EndTextEditing();
            }
            finally
            {
                _replacingRevision = false;
            }
            var current = CurrentLayers();
            if (!LayersEqual(current, _baseline))
                finalSnapshot = current;
        }
        _disposed = true;
        Interlocked.Increment(ref _revisionSelectionGeneration);
        Annotation.Changed -= OnAnnotationChanged;
        CancelDebounce();
        _lifetimeCancellation.Cancel();
        await _flushGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (finalSnapshot is not null)
            {
                var latest = await _repository.GetLatestRevisionSnapshotAsync(
                    Shot.Id,
                    CancellationToken.None).ConfigureAwait(false);
                if (latest is null || !LayersEqual(finalSnapshot, latest.Layers))
                {
                    await _repository.AppendRevisionAsync(
                        Shot.Id,
                        finalSnapshot,
                        $"编辑 {finalSnapshot.Count} 层",
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _flushGate.Release();
            _flushGate.Dispose();
            _lifetimeCancellation.Dispose();
        }
    }
}

public interface IShotEditorSessionFactory
{
    ShotEditorSessionController Create(ShotRecord shot);
}

public sealed class ShotEditorSessionFactory : IShotEditorSessionFactory
{
    private readonly IShotAssetReader _assets;
    private readonly IShotEditorRepository _repository;

    public ShotEditorSessionFactory(
        IShotAssetReader assets,
        IShotEditorRepository repository)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public ShotEditorSessionController Create(ShotRecord shot)
        => new(shot, _assets, _repository);
}
