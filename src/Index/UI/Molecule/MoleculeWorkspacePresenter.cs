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
    private readonly Grid _host;
    private readonly Border _panel;
    private readonly ColumnDefinition _column;
    private readonly TextBlock _status;
    private readonly TextBlock _details;
    private readonly Func<Task> _returnToUiAsync;
    private readonly SemaphoreSlim _scriptGate = new(1, 1);
    private MoleculeSketcherView? _sketcher;
    private Task? _initialization;
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
        OpenBlankAsync(cancellationToken, static () => true);

    public Task OpenBlankAsync(
        CancellationToken cancellationToken,
        Func<bool> isCurrent) =>
        ShowAsync(
            sdf: null,
            smiles: null,
            confidence: 0,
            processingMs: 0,
            cancellationToken,
            isCurrent);

    public Task PresentAsync(
        MoleculeRecognitionResult output,
        CancellationToken cancellationToken = default)
        => PresentAsync(output, cancellationToken, static () => true);

    public Task PresentAsync(
        MoleculeRecognitionResult output,
        CancellationToken cancellationToken,
        Func<bool> isCurrent)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(isCurrent);
        return ShowAsync(
            output.Sdf,
            output.Smiles,
            output.Confidence,
            output.ProcessingTimeMs,
            cancellationToken,
            isCurrent);
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
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ++_generation;
        DisposeSketcher();
    }

    private async Task ShowAsync(
        string? sdf,
        string? smiles,
        double confidence,
        int processingMs,
        CancellationToken cancellationToken,
        Func<bool> isCurrent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var generation = ++_generation;
        await _returnToUiAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (!isCurrent())
            return;

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

        var scriptGateHeld = false;
        try
        {
            await (_initialization ?? Task.CompletedTask);
            await _scriptGate.WaitAsync(cancellationToken);
            scriptGateHeld = true;
            await _returnToUiAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed
                || generation != _generation
                || !isCurrent()
                || _sketcher is null)
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
            cancellationToken.ThrowIfCancellationRequested();
            if (!_disposed && generation == _generation && isCurrent())
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
            if (!_disposed && generation == _generation && isCurrent())
            {
                _status.Text = $"分子编辑器加载失败：{error.Message}";
            }
        }
        finally
        {
            if (scriptGateHeld)
                _scriptGate.Release();
        }
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
