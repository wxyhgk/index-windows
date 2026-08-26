using Index.Storage;

namespace Index.Recognition;

public enum RecognitionWorkflowStatus
{
    Idle,
    Pending,
    Success,
    Empty,
    Error,
    Cancelled
}

public sealed record RecognitionOutputEvaluation(
    RecognitionWorkflowStatus Status,
    string? Error = null)
{
    public static RecognitionOutputEvaluation Success() =>
        new(RecognitionWorkflowStatus.Success);

    public static RecognitionOutputEvaluation Empty() =>
        new(RecognitionWorkflowStatus.Empty);

    public static RecognitionOutputEvaluation Failure(string error) =>
        new(RecognitionWorkflowStatus.Error, error);
}

public interface IRecognitionOutputPolicy<in TOutput>
{
    RecognitionOutputEvaluation Evaluate(TOutput output);
}

public sealed record RecognitionRunResult<TOutput>(
    long RunId,
    RecognitionWorkflowStatus Status,
    RecognitionPluginDescriptor? Plugin,
    TOutput? Output,
    string? Warning,
    string? Error)
    where TOutput : class;

public sealed class ShotRecognitionWorkflow<TOutput> : IDisposable
    where TOutput : class
{
    private readonly object _gate = new();
    private readonly IShotAssetReader _assets;
    private readonly IRecognitionPluginRegistry _plugins;
    private readonly RecognitionCapability _capability;
    private readonly IRecognitionOutputPolicy<TOutput> _outputPolicy;
    private readonly string? _preferredPluginId;
    private CancellationTokenSource? _currentCancellation;
    private long _currentRunId;
    private bool _disposed;

    public ShotRecognitionWorkflow(
        IShotAssetReader assets,
        IRecognitionPluginRegistry plugins,
        RecognitionCapability capability,
        IRecognitionOutputPolicy<TOutput> outputPolicy,
        string? preferredPluginId = null)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
        _capability = capability;
        _outputPolicy = outputPolicy ?? throw new ArgumentNullException(nameof(outputPolicy));
        _preferredPluginId = preferredPluginId;
    }

    public RecognitionWorkflowStatus Status { get; private set; } =
        RecognitionWorkflowStatus.Idle;

    public async Task<RecognitionRunResult<TOutput>> RunAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shot);

        CancellationTokenSource linkedCancellation;
        CancellationTokenSource? previousCancellation;
        long runId;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previousCancellation = _currentCancellation;
            linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _currentCancellation = linkedCancellation;
            runId = ++_currentRunId;
            Status = RecognitionWorkflowStatus.Pending;
        }

        Cancel(previousCancellation);
        var token = linkedCancellation.Token;
        RecognitionPluginDescriptor? selectedPlugin = null;
        string? recognitionWarning = null;

        try
        {
            var plugin = _plugins.GetRequired<TOutput>(
                _capability,
                _preferredPluginId);
            selectedPlugin = plugin.Descriptor;
            var asset = await _assets.ReadBestAvailableAsync(shot, token);
            if (!asset.HasData)
            {
                return Complete(
                    runId,
                    RecognitionWorkflowStatus.Error,
                    plugin.Descriptor,
                    output: null,
                    warning: null,
                    error: asset.Warning ?? "No readable image asset is available.");
            }

            recognitionWarning = asset.Status == ShotAssetStatus.ThumbnailFallback
                ? asset.Warning ?? "The thumbnail was used because the original image is unavailable."
                : null;
            var output = await plugin.RecognizeAsync(
                RecognitionInput.Image(asset.Data, MediaTypeFor(shot, asset.Status)),
                token);
            token.ThrowIfCancellationRequested();

            var evaluation = _outputPolicy.Evaluate(output);
            if (evaluation.Status is not (
                RecognitionWorkflowStatus.Success or
                RecognitionWorkflowStatus.Empty or
                RecognitionWorkflowStatus.Error))
            {
                throw new InvalidOperationException(
                    $"Output policy returned invalid terminal status '{evaluation.Status}'.");
            }

            return Complete(
                runId,
                evaluation.Status,
                plugin.Descriptor,
                output,
                recognitionWarning,
                evaluation.Error);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Complete(
                runId,
                RecognitionWorkflowStatus.Cancelled,
                selectedPlugin,
                output: null,
                recognitionWarning,
                error: null);
        }
        catch (Exception error)
        {
            return Complete(
                runId,
                RecognitionWorkflowStatus.Error,
                selectedPlugin,
                output: null,
                recognitionWarning,
                error: error.Message);
        }
        finally
        {
            lock (_gate)
            {
                if (_currentRunId == runId
                    && ReferenceEquals(_currentCancellation, linkedCancellation))
                {
                    _currentCancellation = null;
                }
            }

            linkedCancellation.Dispose();
        }
    }

    public void CancelCurrent()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            cancellation = _currentCancellation;
            _currentCancellation = null;
            ++_currentRunId;
            Status = RecognitionWorkflowStatus.Cancelled;
        }

        Cancel(cancellation);
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellation = _currentCancellation;
            _currentCancellation = null;
            ++_currentRunId;
            Status = RecognitionWorkflowStatus.Cancelled;
        }

        Cancel(cancellation);
    }

    private RecognitionRunResult<TOutput> Complete(
        long runId,
        RecognitionWorkflowStatus status,
        RecognitionPluginDescriptor? plugin,
        TOutput? output,
        string? warning,
        string? error)
    {
        lock (_gate)
        {
            if (!_disposed && _currentRunId == runId)
            {
                Status = status;
            }
        }

        return new RecognitionRunResult<TOutput>(
            runId,
            status,
            plugin,
            output,
            warning,
            error);
    }

    private static string MediaTypeFor(ShotRecord shot, ShotAssetStatus status)
    {
        if (status == ShotAssetStatus.ThumbnailFallback)
        {
            return "image/jpeg";
        }

        return shot.OriginalExtension.ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            _ => "image/png"
        };
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
    }
}
