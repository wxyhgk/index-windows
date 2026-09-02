using Index.Navigation;
using Microsoft.UI.Xaml;

namespace Index.UI;

internal enum MainPageMount
{
    Destination,
    LibraryBody
}

internal readonly record struct MainPageIdentity(
    MainNavigationPage Page,
    LibrarySection? Section = null);

/// <summary>
/// Owns one mounted destination/body and every disposable resource attached to it.
/// The preview workspace is intentionally owned separately by MainWindow.
/// </summary>
internal sealed class MainPageLease : IDisposable
{
    private readonly List<object> _resources = [];
    private readonly List<Action> _releaseActions = [];
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public MainPageLease(
        MainPageIdentity identity,
        MainPageMount mount,
        FrameworkElement root)
    {
        Identity = identity;
        Mount = mount;
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public MainPageIdentity Identity { get; }
    public MainPageMount Mount { get; }
    public FrameworkElement Root { get; }
    public CancellationToken LifetimeToken => _lifetime.Token;

    public T Own<T>(T resource) where T : class, IDisposable
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(resource);
        _resources.Add(resource);
        _releaseActions.Add(resource.Dispose);
        return resource;
    }

    public void OnDispose(Action cleanup)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(cleanup);
        _releaseActions.Add(cleanup);
    }

    public T? Find<T>() where T : class
    {
        if (Root is T root)
            return root;
        for (int index = _resources.Count - 1; index >= 0; index--)
        {
            if (_resources[index] is T resource)
                return resource;
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        for (int index = _releaseActions.Count - 1; index >= 0; index--)
        {
            try
            {
                _releaseActions[index]();
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine($"Page resource cleanup failed: {error}");
            }
        }

        _releaseActions.Clear();
        _resources.Clear();
        _lifetime.Dispose();
    }
}

/// <summary>Atomically replaces the current destination and releases the previous lease.</summary>
internal sealed class MainPageOwner : IDisposable
{
    private readonly MainContentHost _host;
    private bool _disposed;

    public MainPageOwner(MainContentHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public MainPageLease? Current { get; private set; }

    public T? Find<T>() where T : class => Current?.Find<T>();

    public bool CommitOrDispose(
        MainPageLease candidate,
        int expectedGeneration,
        int currentGeneration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(candidate);
        if (expectedGeneration != currentGeneration)
        {
            candidate.Dispose();
            return false;
        }

        var previous = Current;
        Current = candidate;
        try
        {
            Mount(candidate);
        }
        catch
        {
            Current = previous;
            candidate.Dispose();
            if (previous is not null)
            {
                try
                {
                    Mount(previous);
                }
                catch
                {
                }
            }
            throw;
        }

        previous?.Dispose();
        return true;
    }

    public void Clear()
    {
        var previous = Current;
        Current = null;
        previous?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Clear();
    }

    private void Mount(MainPageLease lease)
    {
        if (lease.Mount == MainPageMount.LibraryBody)
            _host.ShowLibraryBody(lease.Root);
        else
            _host.ShowPage(lease.Root);
    }
}
