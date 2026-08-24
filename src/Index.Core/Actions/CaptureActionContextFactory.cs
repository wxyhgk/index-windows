using System.Globalization;
using Index.Capture;

namespace Index.Actions;

/// <summary>
/// Builds the platform-independent payload passed to capture actions.
/// A caller can share an explicit capture timestamp with persistence, or inject a clock when
/// the context is the operation that establishes the timestamp.
/// </summary>
public sealed class CaptureActionContextFactory
{
    private readonly TimeProvider _clock;

    public CaptureActionContextFactory(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public CaptureContext Create(
        PreparedCaptureImage prepared,
        CaptureRegion region,
        DateTimeOffset capturedAt,
        ICaptureActionHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        return new CaptureContext
        {
            Artifact = prepared.Artifact,
            Region = region,
            SuggestedFileName = SuggestedFileName(capturedAt),
            Host = host
        };
    }

    public CaptureContext CreateNow(
        PreparedCaptureImage prepared,
        CaptureRegion region,
        ICaptureActionHost? host = null) =>
        Create(prepared, region, _clock.GetLocalNow(), host);

    public static string SuggestedFileName(DateTimeOffset capturedAt) =>
        $"Index_{capturedAt.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)}.png";
}
