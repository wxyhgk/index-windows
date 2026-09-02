namespace Index.Storage;

public enum CapturedApplicationsWorkspaceStatus
{
    Idle,
    Loading,
    Overview,
    Application,
    Error,
    Cancelled
}

/// <summary>
/// Immutable state emitted by <see cref="CapturedApplicationsWorkspaceController"/>.
/// UI consumers render this state without owning query or cancellation logic.
/// </summary>
public sealed record CapturedApplicationsWorkspaceState(
    long Generation,
    CapturedApplicationsWorkspaceStatus Status,
    string SearchQuery,
    CapturedApplicationSort Sort,
    string? SelectedApplicationId = null,
    CapturedApplicationOverview? Overview = null,
    CapturedApplicationSummary? SelectedApplication = null,
    ShotPage? FirstPage = null,
    string? Error = null);

public interface ICapturedApplicationsWorkspaceControllerFactory
{
    CapturedApplicationsWorkspaceController Create();
}

public sealed class CapturedApplicationsWorkspaceControllerFactory(
    IShotApplicationSource source) : ICapturedApplicationsWorkspaceControllerFactory
{
    private readonly IShotApplicationSource _source =
        source ?? throw new ArgumentNullException(nameof(source));

    public CapturedApplicationsWorkspaceController Create() => new(_source);
}

/// <summary>
/// Owns the applications workspace query, filtering, selection and async lifecycle.
/// </summary>
public sealed class CapturedApplicationsWorkspaceController : IDisposable
{
    private readonly object _gate = new();
    private readonly IShotApplicationSource _source;
    private CancellationTokenSource? _currentCancellation;
    private IReadOnlyList<CapturedApplicationSummary> _applications = [];
    private CapturedApplicationsWorkspaceState _state = new(
        0,
        CapturedApplicationsWorkspaceStatus.Idle,
        string.Empty,
        CapturedApplicationSort.CaptureCount);
    private string _searchQuery = string.Empty;
    private CapturedApplicationSort _sort = CapturedApplicationSort.CaptureCount;
    private string? _selectedApplicationId;
    private long _generation;
    private bool _disposed;

    public CapturedApplicationsWorkspaceController(IShotApplicationSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public event Action<CapturedApplicationsWorkspaceState>? StateChanged;

    public CapturedApplicationsWorkspaceState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public string SearchQuery
    {
        get
        {
            lock (_gate)
                return _searchQuery;
        }
    }

    public IShotPageSource CreatePageSource(CapturedApplicationIdentity application)
    {
        ArgumentNullException.ThrowIfNull(application);
        lock (_gate)
            ObjectDisposedException.ThrowIf(_disposed, this);
        return new CapturedApplicationPageSource(_source, application);
    }

    public Task LoadOverviewAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(selectedApplicationId: null, cancellationToken);

    public Task OpenApplicationAsync(
        string stableId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        return LoadAsync(stableId, cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        string? selectedApplicationId;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            selectedApplicationId = _selectedApplicationId;
        }

        return LoadAsync(selectedApplicationId, cancellationToken);
    }

    public void SetSearchQuery(string? query)
    {
        CapturedApplicationsWorkspaceState? next = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var normalized = query ?? string.Empty;
            if (string.Equals(_searchQuery, normalized, StringComparison.Ordinal))
                return;

            _searchQuery = normalized;
            if (_selectedApplicationId is null
                && _state.Status == CapturedApplicationsWorkspaceStatus.Overview)
            {
                next = CreateOverviewStateLocked();
                _state = next;
            }
        }

        Notify(next);
    }

    public void SetSort(CapturedApplicationSort sort)
    {
        CapturedApplicationsWorkspaceState? next = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sort == sort)
                return;

            _sort = sort;
            if (_selectedApplicationId is null
                && _state.Status == CapturedApplicationsWorkspaceStatus.Overview)
            {
                next = CreateOverviewStateLocked();
                _state = next;
            }
        }

        Notify(next);
    }

    public void BackToOverview()
    {
        CancellationTokenSource? cancellation;
        CapturedApplicationsWorkspaceState next;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellation = _currentCancellation;
            _currentCancellation = null;
            ++_generation;
            _selectedApplicationId = null;
            next = CreateOverviewStateLocked();
            _state = next;
        }

        Cancel(cancellation);
        Notify(next);
    }

    public void CancelCurrent()
    {
        CancellationTokenSource? cancellation;
        CapturedApplicationsWorkspaceState? cancelled = null;
        lock (_gate)
        {
            if (_disposed)
                return;

            cancellation = _currentCancellation;
            _currentCancellation = null;
            ++_generation;
            if (_state.Status == CapturedApplicationsWorkspaceStatus.Loading)
            {
                cancelled = new CapturedApplicationsWorkspaceState(
                    _generation,
                    CapturedApplicationsWorkspaceStatus.Cancelled,
                    _searchQuery,
                    _sort,
                    _selectedApplicationId);
                _state = cancelled;
            }
        }

        Cancel(cancellation);
        Notify(cancelled);
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

    private async Task LoadAsync(
        string? selectedApplicationId,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource linkedCancellation;
        CancellationTokenSource? previousCancellation;
        CapturedApplicationsWorkspaceState loading;
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previousCancellation = _currentCancellation;
            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _currentCancellation = linkedCancellation;
            generation = ++_generation;
            _selectedApplicationId = selectedApplicationId;
            loading = new CapturedApplicationsWorkspaceState(
                generation,
                CapturedApplicationsWorkspaceStatus.Loading,
                _searchQuery,
                _sort,
                selectedApplicationId);
            _state = loading;
        }

        Cancel(previousCancellation);
        Notify(loading);
        var token = linkedCancellation.Token;

        try
        {
            var applications = await _source.GetCapturedApplicationsAsync(
                cancellationToken: token);
            token.ThrowIfCancellationRequested();

            CapturedApplicationSummary? selected = null;
            ShotPage? firstPage = null;
            if (selectedApplicationId is not null)
            {
                selected = applications.FirstOrDefault(application =>
                    string.Equals(
                        application.StableId,
                        selectedApplicationId,
                        StringComparison.OrdinalIgnoreCase));
                if (selected is not null)
                {
                    firstPage = await _source.GetApplicationPageAsync(
                        selected.Identity,
                        cancellationToken: token);
                    token.ThrowIfCancellationRequested();
                }
            }

            CapturedApplicationsWorkspaceState? completed = null;
            lock (_gate)
            {
                if (!_disposed && generation == _generation)
                {
                    _applications = applications;
                    if (selected is not null && firstPage is not null)
                    {
                        _selectedApplicationId = selected.StableId;
                        completed = new CapturedApplicationsWorkspaceState(
                            generation,
                            CapturedApplicationsWorkspaceStatus.Application,
                            _searchQuery,
                            _sort,
                            selected.StableId,
                            SelectedApplication: selected,
                            FirstPage: firstPage);
                    }
                    else
                    {
                        _selectedApplicationId = null;
                        completed = CreateOverviewStateLocked();
                    }

                    _state = completed;
                }
            }

            Notify(completed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            CapturedApplicationsWorkspaceState? cancelled = null;
            lock (_gate)
            {
                if (!_disposed && generation == _generation)
                {
                    cancelled = new CapturedApplicationsWorkspaceState(
                        generation,
                        CapturedApplicationsWorkspaceStatus.Cancelled,
                        _searchQuery,
                        _sort,
                        selectedApplicationId);
                    _state = cancelled;
                }
            }

            Notify(cancelled);
        }
        catch (Exception error)
        {
            CapturedApplicationsWorkspaceState? failed = null;
            lock (_gate)
            {
                if (!_disposed && generation == _generation)
                {
                    failed = new CapturedApplicationsWorkspaceState(
                        generation,
                        CapturedApplicationsWorkspaceStatus.Error,
                        _searchQuery,
                        _sort,
                        selectedApplicationId,
                        Error: error.Message);
                    _state = failed;
                }
            }

            Notify(failed);
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

    private CapturedApplicationsWorkspaceState CreateOverviewStateLocked() => new(
        _generation,
        CapturedApplicationsWorkspaceStatus.Overview,
        _searchQuery,
        _sort,
        Overview: CapturedApplicationCatalog.Create(
            _applications,
            _searchQuery,
            _sort));

    private void Notify(CapturedApplicationsWorkspaceState? state)
    {
        if (state is null || StateChanged is not { } handlers)
            return;

        foreach (Action<CapturedApplicationsWorkspaceState> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(state);
            }
            catch
            {
                // State observers are output adapters. A broken UI observer must
                // not corrupt the query lifecycle or turn a successful load into
                // a workflow error.
            }
        }
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try
        {
            if (cancellation is not null)
                cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The owning run may have completed between detaching and cancelling.
        }
    }
}
