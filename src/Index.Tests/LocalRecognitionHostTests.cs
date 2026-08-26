using System.Diagnostics;
using Index.Platform;
using Index.Recognition;

namespace Index.Tests;

public sealed class LocalRecognitionHostTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"index-recognition-host-{Guid.NewGuid():N}");

    [Fact]
    public void HealthParserAcceptsVersionedMolGrapher()
    {
        var result = HttpRecognitionHealthProbe.ParseHealth(
            """{"service":"molgrapher","protocol_version":"1","status":"ok"}""",
            8100);

        Assert.Equal(RecognitionHealthState.Compatible, result.State);
        Assert.Equal("1", result.ProtocolVersion);
    }

    [Fact]
    public void KillOnCloseJobTerminatesAssignedProcess()
    {
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = powershell,
            Arguments = "-NoLogo -NoProfile -NonInteractive -Command Start-Sleep -Seconds 30",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Test process did not start.");

        using (var job = WindowsProcessJob.CreateKillOnClose())
            job.Add(process);

        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void HealthParserAcceptsOnlyRecognizableLegacyMolGrapher()
    {
        var legacy = HttpRecognitionHealthProbe.ParseHealth(
            """{"status":"ok","model_loaded":false,"inference_backend":{}}""",
            8100);
        var generic = HttpRecognitionHealthProbe.ParseHealth(
            """{"status":"ok"}""",
            8100);

        Assert.Equal(RecognitionHealthState.Compatible, legacy.State);
        Assert.Equal("legacy", legacy.ProtocolVersion);
        Assert.Equal(RecognitionHealthState.Incompatible, generic.State);
    }

    [Fact]
    public async Task ExistingCompatibleServiceIsAttachedWithoutOwnership()
    {
        var probe = new FakeHealthProbe(
            Compatible("legacy"));
        var launcher = new FakeProcessLauncher();
        using var host = new LocalRecognitionHost(
            Options(Path.Combine(_root, "missing")),
            probe,
            launcher);

        var status = await host.EnsureReadyAsync();

        Assert.True(status.IsReady);
        Assert.False(status.OwnsProcess);
        Assert.Null(status.ProcessId);
        Assert.Equal("legacy", status.ProtocolVersion);
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task UnavailableServiceIsStartedAndOwnedUntilDispose()
    {
        var serviceDirectory = CreateRunnableServiceLayout();
        var probe = new FakeHealthProbe(
            Unavailable(),
            Unavailable(),
            Compatible("1"));
        var process = new FakeProcess(4242);
        var launcher = new FakeProcessLauncher(process);
        var host = new LocalRecognitionHost(
            Options(serviceDirectory),
            probe,
            launcher);

        var status = await host.EnsureReadyAsync();

        Assert.True(status.IsReady);
        Assert.True(status.OwnsProcess);
        Assert.Equal(4242, status.ProcessId);
        Assert.Equal(1, launcher.StartCount);
        Assert.False(process.Killed);

        host.Dispose();
        Assert.True(process.Killed);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task OwnedServiceStopsAfterIdleTimeout()
    {
        var serviceDirectory = CreateRunnableServiceLayout();
        var probe = new FakeHealthProbe(
            Unavailable(),
            Unavailable(),
            Compatible("1"));
        var process = new FakeProcess(4243);
        var options = Options(serviceDirectory);
        options = new MolGrapherHostOptions
        {
            ServiceDirectory = options.ServiceDirectory,
            StartupTimeout = options.StartupTimeout,
            PollInterval = options.PollInterval,
            HealthTimeout = options.HealthTimeout,
            IdleTimeout = TimeSpan.FromMilliseconds(20)
        };
        using var host = new LocalRecognitionHost(
            options,
            probe,
            new FakeProcessLauncher(process));

        var status = await host.EnsureReadyAsync();
        Assert.True(status.OwnsProcess);

        host.NotifyActivityCompleted();
        await WaitUntilAsync(() => process.Killed, TimeSpan.FromSeconds(1));

        Assert.True(process.Killed);
        Assert.Equal(RecognitionServiceHostState.Stopped, host.Status.State);
    }

    [Fact]
    public async Task IncompatibleServiceOnPortIsNotReplaced()
    {
        var probe = new FakeHealthProbe(new RecognitionHealthResult(
            RecognitionHealthState.Incompatible,
            "Port is occupied by another service."));
        var launcher = new FakeProcessLauncher();
        using var host = new LocalRecognitionHost(
            Options(CreateRunnableServiceLayout()),
            probe,
            launcher);

        var status = await host.EnsureReadyAsync();

        Assert.Equal(RecognitionServiceHostState.Failed, status.State);
        Assert.Contains("another service", status.Message);
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task MissingPythonEnvironmentReturnsDiagnosticFailure()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "app.py"), "# test");
        var launcher = new FakeProcessLauncher();
        using var host = new LocalRecognitionHost(
            Options(_root),
            new FakeHealthProbe(Unavailable()),
            launcher);

        var status = await host.EnsureReadyAsync();

        Assert.Equal(RecognitionServiceHostState.Failed, status.State);
        Assert.Contains("Python environment is missing", status.Message);
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task MolGrapherClientStopsBeforeHttpWhenHostIsNotReady()
    {
        var host = new FailedHost("service unavailable");
        using var client = new MolGrapherClient(host: host);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.RecognizeAsync(RecognitionInput.Image(new byte[] { 1, 2, 3 })));

        Assert.Contains("service unavailable", error.Message);
        Assert.Equal(1, host.EnsureCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private MolGrapherHostOptions Options(string serviceDirectory) => new()
    {
        ServiceDirectory = serviceDirectory,
        StartupTimeout = TimeSpan.FromSeconds(2),
        PollInterval = TimeSpan.FromMilliseconds(1),
        HealthTimeout = TimeSpan.FromMilliseconds(100)
    };

    private string CreateRunnableServiceLayout()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app.py"), "# test");
        var scripts = Path.Combine(_root, "venv", "Scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "python.exe"), "test");
        return _root;
    }

    private static RecognitionHealthResult Compatible(string protocol) => new(
        RecognitionHealthState.Compatible,
        "MolGrapher ready.",
        protocol);

    private static RecognitionHealthResult Unavailable() => new(
        RecognitionHealthState.Unavailable,
        "Not listening.");

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private sealed class FakeHealthProbe(
        params RecognitionHealthResult[] results) : IRecognitionHealthProbe
    {
        private readonly Queue<RecognitionHealthResult> _results = new(results);
        private RecognitionHealthResult? _last;

        public Task<RecognitionHealthResult> CheckAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_results.Count > 0)
            {
                _last = _results.Dequeue();
            }

            return Task.FromResult(_last ?? Unavailable());
        }
    }

    private sealed class FakeProcessLauncher(
        FakeProcess? process = null) : IRecognitionProcessLauncher
    {
        private readonly FakeProcess _process = process ?? new FakeProcess(1);

        public int StartCount { get; private set; }

        public IRecognitionProcess Start(
            string pythonExecutable,
            string serviceDirectory,
            string appPath)
        {
            StartCount++;
            return _process;
        }
    }

    private sealed class FakeProcess(int id) : IRecognitionProcess
    {
        public int Id { get; } = id;

        public bool HasExited { get; private set; }

        public int? ExitCode => HasExited ? 0 : null;

        public bool Killed { get; private set; }

        public bool Disposed { get; private set; }

        public void Kill()
        {
            Killed = true;
            HasExited = true;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FailedHost(string message) : IRecognitionServiceHost
    {
        public RecognitionServiceHostStatus Status { get; } = new(
            RecognitionServiceHostState.Failed,
            message);

        public int EnsureCount { get; private set; }

        public Task<RecognitionServiceHostStatus> EnsureReadyAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureCount++;
            return Task.FromResult(Status);
        }

        public void Dispose()
        {
        }
    }
}
