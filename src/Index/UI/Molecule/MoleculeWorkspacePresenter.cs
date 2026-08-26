using Index.Molecule;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Index.UI.Molecule;

internal interface IRecognitionResultPresenter<in TOutput>
{
    Task PresentAsync(
        TOutput output,
        CancellationToken cancellationToken = default);

    void Hide();
}

internal sealed class MoleculeWorkspacePresenter
    : IRecognitionResultPresenter<MoleculeRecognitionResult>, IDisposable
{
    private static readonly TimeSpan IdleDisposeDelay = TimeSpan.FromMinutes(10);
    private readonly Grid _host;
    private readonly Border _panel;
    private readonly ColumnDefinition _column;
    private readonly TextBlock _status;
    private readonly TextBlock _details;
    private readonly Func<Task> _returnToUiAsync;
    private MoleculeSketcherView? _sketcher;
    private Task? _initialization;
    private CancellationTokenSource? _idleDisposeCancellation;
    private int _generation;
    private bool _disposed;

    public MoleculeWorkspacePresenter(
        Grid host,
        Border panel,
        ColumnDefinition column,
        TextBlock status,
        TextBlock details,
        Func<Task> returnToUiAsync)
    {
        _host = host;
        _panel = panel;
        _column = column;
        _status = status;
        _details = details;
        _returnToUiAsync = returnToUiAsync;
    }

    public Task OpenBlankAsync(CancellationToken cancellationToken = default) =>
        ShowAsync(
            sdf: null,
            smiles: null,
            confidence: 0,
            processingMs: 0,
            cancellationToken);

    public Task PresentAsync(
        MoleculeRecognitionResult output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        return ShowAsync(
            output.Sdf,
            output.Smiles,
            output.Confidence,
            output.ProcessingTimeMs,
            cancellationToken);
    }

    public void Hide()
    {
        if (_disposed)
        {
            return;
        }

        ++_generation;
        _panel.Visibility = Visibility.Collapsed;
        _column.Width = new GridLength(0);
        ScheduleIdleDispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ++_generation;
        CancelIdleDispose();
        DisposeSketcher();
    }

    private async Task ShowAsync(
        string? sdf,
        string? smiles,
        double confidence,
        int processingMs,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelIdleDispose();
        var generation = ++_generation;
        await _returnToUiAsync();
        cancellationToken.ThrowIfCancellationRequested();

        _panel.Visibility = Visibility.Visible;
        _column.Width = new GridLength(1.2, GridUnitType.Star);
        _status.Text = "正在准备分子编辑器…";
        _details.Text = sdf is null && smiles is null
            ? "空白画布"
            : $"置信度 {confidence:P0}  ·  {processingMs}ms";
        if (_sketcher is null)
        {
            _sketcher = new MoleculeSketcherView();
            _host.Children.Add(_sketcher);
            _initialization = _sketcher.InitializeAsync();
        }

        try
        {
            await (_initialization ?? Task.CompletedTask);
            await _returnToUiAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || generation != _generation || _sketcher is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(sdf))
            {
                await _sketcher.SetSdfAsync(sdf);
            }
            else if (!string.IsNullOrWhiteSpace(smiles))
            {
                await _sketcher.SetSmilesAsync(smiles);
            }
            else
            {
                await _sketcher.ClearAsync();
            }

            await _returnToUiAsync();
            if (!_disposed && generation == _generation)
            {
                _status.Text = sdf is null && smiles is null
                    ? "空白画布已就绪"
                    : "识别结果已载入，可以对照原图编辑";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await _returnToUiAsync();
            if (!_disposed && generation == _generation)
            {
                _status.Text = $"分子编辑器加载失败：{error.Message}";
            }
        }
    }

    private void ScheduleIdleDispose()
    {
        CancelIdleDispose();
        if (_sketcher is null)
            return;

        var cancellation = new CancellationTokenSource();
        _idleDisposeCancellation = cancellation;
        _ = DisposeSketcherAfterIdleAsync(cancellation);
    }

    private async Task DisposeSketcherAfterIdleAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(IdleDisposeDelay, cancellation.Token)
                .ConfigureAwait(false);
            await _returnToUiAsync();
            if (!_disposed
                && ReferenceEquals(_idleDisposeCancellation, cancellation)
                && _panel.Visibility == Visibility.Collapsed)
            {
                _idleDisposeCancellation = null;
                DisposeSketcher();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_idleDisposeCancellation, cancellation))
                _idleDisposeCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelIdleDispose()
    {
        var cancellation = _idleDisposeCancellation;
        _idleDisposeCancellation = null;
        cancellation?.Cancel();
    }

    private void DisposeSketcher()
    {
        if (_sketcher is null)
            return;

        _host.Children.Remove(_sketcher);
        _sketcher.Dispose();
        _sketcher = null;
        _initialization = null;
    }
}
