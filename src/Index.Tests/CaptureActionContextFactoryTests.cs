using Index.Actions;
using Index.Annotation;
using Index.Capture;

namespace Index.Tests;

public sealed class CaptureActionContextFactoryTests
{
    [Fact]
    public void Create_WithCapturedAt_BuildsStableContextAndSuggestedFileName()
    {
        var artifact = new CaptureArtifact([1, 2, 3], new Layers<ImageSpace>());
        var prepared = new PreparedCaptureImage([1, 2, 3], artifact);
        var region = new CaptureRegion(10, 20, 300, 200);
        var host = new RecordingHost();
        var capturedAt = new DateTimeOffset(
            2026,
            8,
            24,
            9,
            10,
            11,
            123,
            TimeSpan.FromHours(8));

        var context = new CaptureActionContextFactory().Create(
            prepared,
            region,
            capturedAt,
            host);

        Assert.Same(artifact, context.Artifact);
        Assert.Equal(region, context.Region);
        Assert.Equal("Index_20260824_091011_123.png", context.SuggestedFileName);
        Assert.Same(host, context.Host);
    }

    [Fact]
    public void CreateNow_UsesInjectedClockLocalTime()
    {
        var utcNow = new DateTimeOffset(2026, 8, 24, 1, 2, 3, 456, TimeSpan.Zero);
        var clock = new FixedTimeProvider(utcNow, TimeSpan.FromHours(8));
        var artifact = new CaptureArtifact([1], new Layers<ImageSpace>());
        var prepared = new PreparedCaptureImage([1], artifact);

        var context = new CaptureActionContextFactory(clock).CreateNow(
            prepared,
            new CaptureRegion(1, 2, 3, 4));

        Assert.Equal("Index_20260824_090203_456.png", context.SuggestedFileName);
    }

    [Fact]
    public void Create_RejectsMissingPreparedImage()
    {
        var factory = new CaptureActionContextFactory();

        Assert.Throws<ArgumentNullException>(() => factory.Create(
            null!,
            new CaptureRegion(0, 0, 1, 1),
            DateTimeOffset.UnixEpoch));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private readonly TimeZoneInfo _localTimeZone;

        public FixedTimeProvider(DateTimeOffset utcNow, TimeSpan localOffset)
        {
            _utcNow = utcNow.ToUniversalTime();
            _localTimeZone = TimeZoneInfo.CreateCustomTimeZone(
                $"Test-{localOffset}",
                localOffset,
                "Test local time",
                "Test local time");
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => _localTimeZone;
    }

    private sealed class RecordingHost : ICaptureActionHost
    {
        public void Dismiss() { }
    }
}
