using Index.Annotation;
using Index.Capture;
using Index.Platform;
using Index.Storage;

namespace Index.Tests;

public sealed class CapturePersistenceServiceTests
{
    [Fact]
    public async Task SaveAsync_ResolvesSourceAndMapsStorageMetadata()
    {
        var application = new SourceApplicationInfo(
            42,
            "Browser",
            "browser.exe",
            "Example");
        var writer = new RecordingWriter();
        var resolver = new RecordingSourceResolver(application);
        var browser = new RecordingBrowserResolver("https://example.com/");
        var service = new CapturePersistenceService(writer, resolver, browser);
        var layers = new Layers<ImageSpace>();
        var selection = new CaptureSelection
        {
            Display = new CaptureDisplayIdentity("display", 0, 0, 200, 100, 1.5),
            X = 5,
            Y = 6,
            Width = 40,
            Height = 30,
            Layers = layers
        };
        var png = new byte[] { 1, 2, 3 };
        var artifact = new CaptureArtifact(png, layers);
        var display = new DisplaySnapshot
        {
            DisplayId = "display",
            DeviceName = "Monitor",
            DisplayIndex = 2,
            Width = 200,
            Height = 100,
            DpiScale = 1.5,
            Left = 100,
            Top = 50,
            PngData = png
        };
        var bounds = new SourceWindowBounds(105, 56, 145, 86);
        var capturedAt = new DateTimeOffset(2026, 8, 24, 1, 2, 3, TimeSpan.Zero);

        await service.SaveAsync(new CapturePersistenceRequest(
            new PreparedCaptureImage(png, artifact),
            selection,
            display,
            bounds,
            new SourceApplicationSnapshot(null, []),
            capturedAt));

        Assert.Equal(bounds, resolver.Region);
        Assert.Same(application, browser.Application);
        Assert.True(png.AsSpan().SequenceEqual(writer.Png.Span));
        Assert.Same(layers, writer.Layers);
        Assert.NotNull(writer.Metadata);
        Assert.Equal(capturedAt, writer.Metadata.CapturedAt);
        Assert.Equal(1.5, writer.Metadata.Scale);
        Assert.Equal("Browser", writer.Metadata.AppName);
        Assert.Equal("browser.exe", writer.Metadata.AppIdentifier);
        Assert.Equal("Example", writer.Metadata.WindowTitle);
        Assert.Equal("https://example.com/", writer.Metadata.SourceUrl);
        Assert.Equal(2, writer.Metadata.DisplayIndex);
        Assert.Equal(105, writer.Metadata.RegionX);
        Assert.Equal(56, writer.Metadata.RegionY);
        Assert.Equal(40, writer.Metadata.RegionWidth);
        Assert.Equal(30, writer.Metadata.RegionHeight);
    }

    [Fact]
    public async Task SaveAsync_ForwardsCancellationToMetadataAndStorage()
    {
        var writer = new RecordingWriter();
        var browser = new RecordingBrowserResolver(null);
        var service = new CapturePersistenceService(
            writer,
            new RecordingSourceResolver(null),
            browser);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var layers = new Layers<ImageSpace>();
        var selection = new CaptureSelection
        {
            Display = new CaptureDisplayIdentity("display", 0, 0, 1, 1, 1),
            X = 0,
            Y = 0,
            Width = 1,
            Height = 1,
            Layers = layers
        };
        var png = new byte[] { 1 };
        var request = new CapturePersistenceRequest(
            new PreparedCaptureImage(png, new CaptureArtifact(png, layers)),
            selection,
            new DisplaySnapshot
            {
                DisplayId = "display",
                DeviceName = "Monitor",
                DisplayIndex = 0,
                Width = 1,
                Height = 1,
                DpiScale = 1,
                Left = 0,
                Top = 0,
                PngData = png
            },
            new SourceWindowBounds(0, 0, 1, 1),
            new SourceApplicationSnapshot(null, []),
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SaveAsync(request, cancellation.Token));

        Assert.Equal(cancellation.Token, browser.CancellationToken);
        Assert.Null(writer.Metadata);
    }

    private sealed class RecordingSourceResolver(SourceApplicationInfo? application)
        : ISourceApplicationResolver
    {
        public SourceWindowBounds? Region { get; private set; }

        public SourceApplicationSnapshot CaptureSnapshot() => new(null, []);

        public SourceApplicationInfo? Resolve(
            SourceApplicationSnapshot snapshot,
            SourceWindowBounds selectedRegion)
        {
            Region = selectedRegion;
            return application;
        }
    }

    private sealed class RecordingBrowserResolver(string? url) : IBrowserSourceMetadataResolver
    {
        public SourceApplicationInfo? Application { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<string?> ResolveUrlAsync(
            SourceApplicationInfo? application,
            CancellationToken cancellationToken = default)
        {
            Application = application;
            CancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(url);
        }
    }

    private sealed class RecordingWriter : IShotCaptureWriter
    {
        public ReadOnlyMemory<byte> Png { get; private set; }
        public ShotCaptureMetadata? Metadata { get; private set; }
        public Layers<ImageSpace>? Layers { get; private set; }

        public Task<StoredCapture> SaveCaptureAsync(
            ReadOnlyMemory<byte> originalPng,
            ShotCaptureMetadata metadata,
            Layers<ImageSpace> layers,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Png = originalPng;
            Metadata = metadata;
            Layers = layers;
            return Task.FromResult(new StoredCapture(
                new ShotRecord(
                    1,
                    "sha",
                    metadata.CapturedAt,
                    metadata.RegionWidth,
                    metadata.RegionHeight,
                    metadata.Scale,
                    metadata.AppName,
                    metadata.AppIdentifier,
                    metadata.WindowTitle,
                    metadata.SourceUrl,
                    metadata.DisplayIndex,
                    metadata.DisplayName,
                    metadata.RegionX,
                    metadata.RegionY,
                    metadata.RegionWidth,
                    metadata.RegionHeight,
                    "png"),
                new RevisionRecord(1, 1, null, metadata.CapturedAt, null, "[]")));
        }
    }
}
