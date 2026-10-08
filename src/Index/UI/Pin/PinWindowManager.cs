using Index.Actions;
using Index.Ocr;
using Index.Pin;
using Index.Platform.Clipboard;
using Index.Platform.Diagnostics;
using Index.Render;
using Microsoft.UI.Dispatching;

namespace Index.UI.Pin;

/// <summary>Owns all independent pin windows for the lifetime of the application.</summary>
public sealed class PinWindowManager : IPinPresenter, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly CaptureActionRegistry _actions;
    private readonly IOcrTextRecognizer _ocrTextRecognizer;
    private readonly IClipboardWriter _clipboardWriter;
    private readonly IAppDiagnostics _diagnostics;
    private readonly HashSet<PinWindow> _windows = new();
    private bool _disposed;

    public PinWindowManager(
        CaptureActionRegistry actions,
        IOcrTextRecognizer ocrTextRecognizer,
        IClipboardWriter clipboardWriter,
        IAppDiagnostics? diagnostics = null,
        DispatcherQueue? dispatcher = null)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _ocrTextRecognizer = ocrTextRecognizer
            ?? throw new ArgumentNullException(nameof(ocrTextRecognizer));
        _clipboardWriter = clipboardWriter
            ?? throw new ArgumentNullException(nameof(clipboardWriter));
        _diagnostics = diagnostics ?? NullAppDiagnostics.Instance;
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
        var window = new PinWindow(
            model,
            _actions,
            _ocrTextRecognizer,
            _clipboardWriter,
            _diagnostics);
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        return ShowAndStartOcrAsync(window);
    }

    private static async Task ShowAndStartOcrAsync(PinWindow window)
    {
        await window.ShowAsync();
        window.StartAutomaticOcr();
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
