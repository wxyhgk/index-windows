using Index.Actions;
using Index.Pin;
using Index.Render;
using Microsoft.UI.Dispatching;

namespace Index.UI.Pin;

/// <summary>Owns all independent pin windows for the lifetime of the application.</summary>
public sealed class PinWindowManager : IPinPresenter, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly CaptureActionRegistry _actions;
    private readonly HashSet<PinWindow> _windows = new();
    private bool _disposed;

    public PinWindowManager(CaptureActionRegistry actions, DispatcherQueue? dispatcher = null)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _dispatcher = dispatcher
            ?? DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("PinWindowManager must be created on the UI thread.");
    }

    public async ValueTask PresentAsync(
        CaptureContext context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);

        var rendered = await context.Artifact
            .GetRenderedPngAsync(cancellationToken)
            .ConfigureAwait(false);
        byte[] png = rendered.ToArray();
        var pixels = PinPixelRenderer.DecodePng(png);

        var model = new PinWindowModel(
            context.Artifact,
            png,
            pixels,
            context.Region,
            context.SuggestedFileName);
        Task presentation = Task.CompletedTask;
        await InvokeOnUiThreadAsync(
            () => presentation = Present(model),
            cancellationToken).ConfigureAwait(false);
        await presentation.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var window in _windows.ToArray())
            window.Dismiss();
        _windows.Clear();
    }

    private Task Present(PinWindowModel model)
    {
        if (_disposed) return Task.CompletedTask;
        var window = new PinWindow(model, _actions);
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        return window.ShowAsync();
    }

    private Task InvokeOnUiThreadAsync(Action action, CancellationToken cancellationToken)
    {
        if (_dispatcher.HasThreadAccess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    action();
                    completion.SetResult();
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
            }))
            completion.SetException(new InvalidOperationException("The UI dispatcher is shutting down."));
        return completion.Task;
    }
}
