using System.Diagnostics;
using System.Text.Json;
using Index.Recognition;

namespace Index.Platform;

public sealed class MolGrapherHostOptions
{
    public Uri BaseUri { get; init; } = new("http://127.0.0.1:8100");

    public string? ServiceDirectory { get; init; }

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);
}

public sealed class LocalRecognitionHost : IRecognitionServiceHost
{
    private readonly object _statusGate = new();
    private readonly SemaphoreSlim _prepareGate = new(1, 1);
    private readonly MolGrapherHostOptions _options;
    private readonly IRecognitionHealthProbe _healthProbe;
    private readonly IRecognitionProcessLauncher _processLauncher;
    private IRecognitionProcess? _ownedProcess;
    private CancellationTokenSource? _idleStopCancellation;
    private RecognitionServiceHostStatus _status = new(
        RecognitionServiceHostState.Stopped,
        "MolGrapher service has not been checked.");
    private bool _disposed;

    public LocalRecognitionHost(MolGrapherHostOptions? options = null)
    {
        _options = options ?? new MolGrapherHostOptions();
        _healthProbe = new HttpRecognitionHealthProbe(_options);
        _processLauncher = new RecognitionProcessLauncher();
    }

    internal LocalRecognitionHost(
        MolGrapherHostOptions options,
        IRecognitionHealthProbe healthProbe,
        IRecognitionProcessLauncher processLauncher)
    {
        _options = options;
        _healthProbe = healthProbe;
        _processLauncher = processLauncher;
    }

    public RecognitionServiceHostStatus Status
    {
        get
        {
            lock (_statusGate)
            {
                return _status;
            }
        }
    }

    public async Task<RecognitionServiceHostStatus> EnsureReadyAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelIdleStop();
        await _prepareGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            SetStatus(new RecognitionServiceHostStatus(
                RecognitionServiceHostState.Checking,
                "Checking the MolGrapher service."));

            var health = await _healthProbe.CheckAsync(cancellationToken);
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (health.State == RecognitionHealthState.Compatible)
            {
                var process = GetOwnedProcess();
                var owned = process is { HasExited: false };
                return SetStatus(new RecognitionServiceHostStatus(
                    RecognitionServiceHostState.Ready,
                    health.Message,
                    owned,
                    owned ? process!.Id : null,
                    health.ProtocolVersion));
            }

            if (health.State == RecognitionHealthState.Incompatible)
            {
                return SetStatus(new RecognitionServiceHostStatus(
                    RecognitionServiceHostState.Failed,
                    health.Message));
            }

            var existingOwnedProcess = GetOwnedProcess();
            if (existingOwnedProcess is { HasExited: false })
            {
                return await WaitUntilReadyAsync(
                    existingOwnedProcess,
                    cancellationToken);
            }

            DisposeExitedProcess();
            var launch = ResolveLaunch();
            if (launch.Error is not null)
            {
                return SetStatus(new RecognitionServiceHostStatus(
                    RecognitionServiceHostState.Failed,
                    launch.Error));
            }

            IRecognitionProcess startedProcess;
            try
            {
                startedProcess = _processLauncher.Start(
                    launch.PythonExecutable!,
                    launch.ServiceDirectory!,
                    launch.AppPath!);
                if (!TryAdoptProcess(startedProcess))
                {
                    StopOwnedProcess(startedProcess);
                    throw new ObjectDisposedException(nameof(LocalRecognitionHost));
                }
            }
            catch (Exception error)
            {
                return SetStatus(new RecognitionServiceHostStatus(
                    RecognitionServiceHostState.Failed,
                    $"Failed to start MolGrapher: {error.Message}"));
            }

            SetStatus(new RecognitionServiceHostStatus(
                RecognitionServiceHostState.Starting,
                    $"Starting MolGrapher from '{launch.ServiceDirectory}'.",
                    OwnsProcess: true,
                    ProcessId: startedProcess.Id));
            return await WaitUntilReadyAsync(
                startedProcess,
                cancellationToken);
        }
        finally
        {
            _prepareGate.Release();
        }
    }

    public void Dispose()
    {
        CancelIdleStop();
        IRecognitionProcess? process;
        lock (_statusGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            process = _ownedProcess;
            _ownedProcess = null;
            _status = new RecognitionServiceHostStatus(
                RecognitionServiceHostState.Disposed,
                "MolGrapher host was disposed.");
        }

        StopOwnedProcess(process);
    }

    public void NotifyActivityCompleted()
    {
        CancellationTokenSource? previous;
        CancellationTokenSource cancellation;
        IRecognitionProcess? process;
        lock (_statusGate)
        {
            if (_disposed || _ownedProcess is not { HasExited: false } owned)
                return;

            process = owned;
            previous = _idleStopCancellation;
            cancellation = new CancellationTokenSource();
            _idleStopCancellation = cancellation;
        }

        previous?.Cancel();
        _ = StopAfterIdleAsync(process, cancellation);
    }

    private async Task StopAfterIdleAsync(
        IRecognitionProcess process,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(_options.IdleTimeout, cancellation.Token)
                .ConfigureAwait(false);
            await _prepareGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                var shouldStop = false;
                lock (_statusGate)
                {
                    if (!_disposed
                        && ReferenceEquals(_idleStopCancellation, cancellation)
                        && ReferenceEquals(_ownedProcess, process))
                    {
                        _idleStopCancellation = null;
                        _ownedProcess = null;
                        _status = new RecognitionServiceHostStatus(
                            RecognitionServiceHostState.Stopped,
                            "MolGrapher stopped after being idle.");
                        shouldStop = true;
                    }
                }

                if (shouldStop)
                    StopOwnedProcess(process);
            }
            finally
            {
                _prepareGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_statusGate)
            {
                if (ReferenceEquals(_idleStopCancellation, cancellation))
                    _idleStopCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void CancelIdleStop()
    {
        CancellationTokenSource? cancellation;
        lock (_statusGate)
        {
            cancellation = _idleStopCancellation;
            _idleStopCancellation = null;
        }
        cancellation?.Cancel();
    }

    private async Task<RecognitionServiceHostStatus> WaitUntilReadyAsync(
        IRecognitionProcess process,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _options.StartupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                var exitCode = process.ExitCode;
                process.Dispose();
                ClearOwnedProcess(process);

                return SetStatus(new RecognitionServiceHostStatus(
                    RecognitionServiceHostState.Failed,
                    $"MolGrapher exited before becoming ready (exit code {exitCode?.ToString() ?? "unknown"})."));
            }

            var health = await _healthProbe.CheckAsync(cancellationToken);
            if (health.State == RecognitionHealthState.Compatible)
            {
                return SetStatus(new RecognitionServiceHostStatus(
                    RecognitionServiceHostState.Ready,
                    health.Message,
                    OwnsProcess: true,
                    ProcessId: process.Id,
                    ProtocolVersion: health.ProtocolVersion));
            }

            if (health.State == RecognitionHealthState.Incompatible)
            {
                StopOwnedProcess(process);
                ClearOwnedProcess(process);

                return SetStatus(new RecognitionServiceHostStatus(
                    RecognitionServiceHostState.Failed,
                    health.Message));
            }

            await Task.Delay(_options.PollInterval, cancellationToken);
        }

        StopOwnedProcess(process);
        ClearOwnedProcess(process);

        return SetStatus(new RecognitionServiceHostStatus(
            RecognitionServiceHostState.Failed,
            $"MolGrapher did not become ready within {_options.StartupTimeout.TotalSeconds:0} seconds."));
    }

    private RecognitionServiceHostStatus SetStatus(RecognitionServiceHostStatus status)
    {
        lock (_statusGate)
        {
            if (!_disposed)
            {
                _status = status;
            }

            return _status;
        }
    }

    private LaunchResolution ResolveLaunch()
    {
        var serviceDirectory = ResolveServiceDirectory();
        if (serviceDirectory is null)
        {
            return LaunchResolution.Failed(
                "MolGrapher service directory was not found. Set INDEX_MOLGRAPHER_SERVICE_DIR or install it beside Index.exe.");
        }

        var appPath = Path.Combine(serviceDirectory, "app.py");
        if (!File.Exists(appPath))
        {
            return LaunchResolution.Failed($"MolGrapher entry point is missing: {appPath}");
        }

        var pythonExecutable = Path.Combine(
            serviceDirectory,
            "venv",
            "Scripts",
            "python.exe");
        if (!File.Exists(pythonExecutable))
        {
            return LaunchResolution.Failed(
                $"MolGrapher Python environment is missing: {pythonExecutable}. Run molgrapher-service\\start.bat once to install dependencies.");
        }

        return new LaunchResolution(
            serviceDirectory,
            appPath,
            pythonExecutable,
            Error: null);
    }

    private string? ResolveServiceDirectory()
    {
        var configured = _options.ServiceDirectory;
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable(
                "INDEX_MOLGRAPHER_SERVICE_DIR");
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && directory is not null; depth++)
        {
            var candidate = Path.Combine(directory.FullName, "molgrapher-service");
            if (File.Exists(Path.Combine(candidate, "app.py")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private void DisposeExitedProcess()
    {
        var process = GetOwnedProcess();
        if (process is not { HasExited: true })
        {
            return;
        }

        process.Dispose();
        ClearOwnedProcess(process);
    }

    private IRecognitionProcess? GetOwnedProcess()
    {
        lock (_statusGate)
        {
            return _ownedProcess;
        }
    }

    private bool TryAdoptProcess(IRecognitionProcess process)
    {
        lock (_statusGate)
        {
            if (_disposed)
            {
                return false;
            }

            _ownedProcess = process;
            return true;
        }
    }

    private void ClearOwnedProcess(IRecognitionProcess process)
    {
        lock (_statusGate)
        {
            if (ReferenceEquals(_ownedProcess, process))
            {
                _ownedProcess = null;
            }
        }
    }

    private static void StopOwnedProcess(IRecognitionProcess? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private sealed record LaunchResolution(
        string? ServiceDirectory,
        string? AppPath,
        string? PythonExecutable,
        string? Error)
    {
        public static LaunchResolution Failed(string error) =>
            new(null, null, null, error);
    }
}

internal enum RecognitionHealthState
{
    Unavailable,
    Compatible,
    Incompatible
}

internal sealed record RecognitionHealthResult(
    RecognitionHealthState State,
    string Message,
    string? ProtocolVersion = null);

internal interface IRecognitionHealthProbe
{
    Task<RecognitionHealthResult> CheckAsync(
        CancellationToken cancellationToken = default);
}

internal sealed class HttpRecognitionHealthProbe : IRecognitionHealthProbe
{
    private static readonly HttpClient Http = new();
    private readonly MolGrapherHostOptions _options;

    public HttpRecognitionHealthProbe(MolGrapherHostOptions options)
    {
        _options = options;
    }

    public async Task<RecognitionHealthResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_options.HealthTimeout);
        try
        {
            using var response = await Http.GetAsync(
                new Uri(_options.BaseUri, "/health"),
                timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new RecognitionHealthResult(
                    RecognitionHealthState.Incompatible,
                    $"Port {_options.BaseUri.Port} responded, but /health returned HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return ParseHealth(body, _options.BaseUri.Port);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new RecognitionHealthResult(
                RecognitionHealthState.Unavailable,
                "MolGrapher health check timed out.");
        }
        catch (HttpRequestException)
        {
            return new RecognitionHealthResult(
                RecognitionHealthState.Unavailable,
                "MolGrapher is not listening.");
        }
        catch (JsonException)
        {
            return new RecognitionHealthResult(
                RecognitionHealthState.Incompatible,
                $"Port {_options.BaseUri.Port} returned invalid health JSON.");
        }
    }

    internal static RecognitionHealthResult ParseHealth(string body, int port)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (!root.TryGetProperty("status", out var status)
            || !string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
        {
            return new RecognitionHealthResult(
                RecognitionHealthState.Incompatible,
                $"Port {port} does not expose a compatible MolGrapher health response.");
        }

        if (!root.TryGetProperty("service", out var service))
        {
            var looksLikeLegacyMolGrapher = root.TryGetProperty(
                    "model_loaded",
                    out _)
                && root.TryGetProperty("inference_backend", out _);
            return looksLikeLegacyMolGrapher
                ? new RecognitionHealthResult(
                    RecognitionHealthState.Compatible,
                    "Connected to an existing legacy MolGrapher service.",
                    ProtocolVersion: "legacy")
                : new RecognitionHealthResult(
                    RecognitionHealthState.Incompatible,
                    $"Port {port} returned a generic health response, not MolGrapher.");
        }

        if (!string.Equals(
            service.GetString(),
            "molgrapher",
            StringComparison.OrdinalIgnoreCase))
        {
            return new RecognitionHealthResult(
                RecognitionHealthState.Incompatible,
                $"Port {port} is occupied by a different service.");
        }

        var protocol = root.TryGetProperty("protocol_version", out var version)
            ? version.GetString()
            : null;
        if (protocol != "1")
        {
            return new RecognitionHealthResult(
                RecognitionHealthState.Incompatible,
                $"MolGrapher protocol '{protocol ?? "missing"}' is not supported.",
                protocol);
        }

        return new RecognitionHealthResult(
            RecognitionHealthState.Compatible,
            "MolGrapher protocol 1 is ready.",
            protocol);
    }
}

internal interface IRecognitionProcessLauncher
{
    IRecognitionProcess Start(
        string pythonExecutable,
        string serviceDirectory,
        string appPath);
}

internal interface IRecognitionProcess : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    int? ExitCode { get; }

    void Kill();
}

internal sealed class RecognitionProcessLauncher : IRecognitionProcessLauncher
{
    public IRecognitionProcess Start(
        string pythonExecutable,
        string serviceDirectory,
        string appPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            WorkingDirectory = serviceDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(appPath);
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Python process did not start.");
        WindowsProcessJob? job = null;
        try
        {
            job = WindowsProcessJob.CreateKillOnClose();
            job.Add(process);
            return new RecognitionProcess(process, job);
        }
        catch
        {
            job?.Dispose();
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            process.Dispose();
            throw;
        }
    }
}

internal sealed class RecognitionProcess : IRecognitionProcess
{
    private readonly Process _process;
    private readonly WindowsProcessJob _job;

    public RecognitionProcess(Process process, WindowsProcessJob job)
    {
        _process = process;
        _job = job;
    }

    public int Id => _process.Id;

    public bool HasExited => _process.HasExited;

    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    public void Kill() => _process.Kill(entireProcessTree: true);

    public void Dispose()
    {
        _job.Dispose();
        _process.Dispose();
    }
}
