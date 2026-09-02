using System.Text.Json;
using Index.Platform;
using Index.Platform.Diagnostics;

namespace Index.Tests;

public sealed class LocalAppDiagnosticsTests
{
    [Fact]
    public void WriteCreatesStructuredLogAndRedactsTitlesByDefault()
    {
        using var directory = new TemporaryDirectory();
        var diagnostics = new LocalAppDiagnostics(directory.Path);

        diagnostics.Write(
            AppDiagnosticLevel.Information,
            "capture",
            "target-selected",
            new Dictionary<string, string?>
            {
                ["windowTitle"] = "Private document - Microsoft Edge",
                ["displayId"] = "display-1",
            });

        string line = Assert.Single(File.ReadAllLines(System.IO.Path.Combine(directory.Path, "index.log")));
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;

        Assert.Equal("Information", root.GetProperty("Level").GetString());
        Assert.Equal("capture", root.GetProperty("Category").GetString());
        Assert.Equal("target-selected", root.GetProperty("EventName").GetString());
        Assert.Equal("[redacted]", root.GetProperty("Properties").GetProperty("windowTitle").GetString());
        Assert.Equal("display-1", root.GetProperty("Properties").GetProperty("displayId").GetString());
        Assert.DoesNotContain("Private document", line, StringComparison.Ordinal);
    }

    [Fact]
    public void SensitiveValuesCanBeExplicitlyEnabled()
    {
        using var directory = new TemporaryDirectory();
        var diagnostics = new LocalAppDiagnostics(directory.Path, includeSensitiveData: true);

        diagnostics.Write(
            AppDiagnosticLevel.Trace,
            "capture",
            "window-inspected",
            new Dictionary<string, string?> { ["sourceTitle"] = "Visible when opted in" });

        string log = File.ReadAllText(System.IO.Path.Combine(directory.Path, "index.log"));
        Assert.Contains("Visible when opted in", log, StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionDetailsAreOmittedByDefault()
    {
        using var directory = new TemporaryDirectory();
        var diagnostics = new LocalAppDiagnostics(directory.Path);

        diagnostics.Write(
            AppDiagnosticLevel.Error,
            "capture",
            "failed",
            exception: new InvalidOperationException("Private window title"));

        string log = File.ReadAllText(System.IO.Path.Combine(directory.Path, "index.log"));
        Assert.Contains(typeof(InvalidOperationException).FullName!, log, StringComparison.Ordinal);
        Assert.DoesNotContain("Private window title", log, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteRollsBoundedFiles()
    {
        using var directory = new TemporaryDirectory();
        const int maxFileBytes = 512;
        var diagnostics = new LocalAppDiagnostics(
            directory.Path,
            maxFileBytes: maxFileBytes,
            maxArchiveFiles: 2);

        for (int index = 0; index < 40; index++)
        {
            diagnostics.Write(
                AppDiagnosticLevel.Information,
                "capture",
                "frame-ready",
                new Dictionary<string, string?> { ["payload"] = new string('x', 96) });
        }

        string[] files = Directory.GetFiles(directory.Path, "index*.log");
        Assert.Equal(3, files.Length);
        Assert.All(files, path => Assert.InRange(new FileInfo(path).Length, 1, maxFileBytes));
    }

    [Fact]
    public void ConcurrentWritesDoNotLoseOrInterleaveEvents()
    {
        using var directory = new TemporaryDirectory();
        var diagnostics = new LocalAppDiagnostics(directory.Path, maxFileBytes: 1024 * 1024);

        Parallel.For(
            0,
            200,
            index => diagnostics.Write(
                AppDiagnosticLevel.Trace,
                "test",
                $"event-{index}"));

        string[] lines = File.ReadAllLines(System.IO.Path.Combine(directory.Path, "index.log"));
        Assert.Equal(200, lines.Length);
        Assert.All(lines, line => JsonDocument.Parse(line).Dispose());
    }

    [Fact]
    public void StorageFailureNeverEscapesToCaller()
    {
        using var directory = new TemporaryDirectory();
        string fileInsteadOfDirectory = System.IO.Path.Combine(directory.Path, "not-a-directory");
        File.WriteAllText(fileInsteadOfDirectory, "occupied");
        var diagnostics = new LocalAppDiagnostics(fileInsteadOfDirectory);

        Exception? error = Record.Exception(() => diagnostics.Write(
            AppDiagnosticLevel.Error,
            "startup",
            "failed",
            exception: new InvalidOperationException("original failure")));

        Assert.Null(error);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Index.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Test cleanup is best effort on Windows where scanners may briefly retain handles.
            }
        }
    }
}
