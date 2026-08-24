using Index.Annotation;
using Index.Capture;
using Index.Platform;

namespace Index.Tests;

public sealed class CaptureDecisionBindingTests
{
    [Fact]
    public void ResolveSnapshot_UsesSelectionDisplayInsteadOfFirstSnapshot()
    {
        var first = Snapshot("display-a", 0, 0, 1920, 1080, 1);
        var selected = Snapshot("display-b", 1920, -200, 2560, 1440, 1.5);
        var decision = Decision(CaptureDecisionBinding.IdentityOf(selected));

        var resolved = CaptureDecisionBinding.ResolveSnapshot(decision, [first, selected]);

        Assert.Same(selected, resolved);
    }

    [Fact]
    public void ResolveSnapshot_RejectsDisplayOutsideFrozenBatch()
    {
        var frozen = Snapshot("display-a", 0, 0, 1920, 1080, 1);
        var unknown = Snapshot("display-b", 1920, 0, 1920, 1080, 1);

        var error = Assert.Throws<InvalidOperationException>(() =>
            CaptureDecisionBinding.ResolveSnapshot(
                Decision(CaptureDecisionBinding.IdentityOf(unknown)),
                [frozen]));

        Assert.Contains("not part of the frozen batch", error.Message);
    }

    [Fact]
    public void ResolveSnapshot_RejectsChangedBoundsOrDpiForSameDisplayId()
    {
        var frozen = Snapshot("display-a", 0, 0, 1920, 1080, 1);
        var staleIdentity = new CaptureDisplayIdentity(
            "DISPLAY-A", 0, 0, 2560, 1440, 1.5);

        var error = Assert.Throws<InvalidOperationException>(() =>
            CaptureDecisionBinding.ResolveSnapshot(Decision(staleIdentity), [frozen]));

        Assert.Contains("topology changed", error.Message);
    }

    [Fact]
    public void ResolveSnapshot_RejectsDuplicateStableDisplayIds()
    {
        var first = Snapshot("display-a", 0, 0, 1920, 1080, 1);
        var duplicate = Snapshot("DISPLAY-A", 1920, 0, 1920, 1080, 1);

        var error = Assert.Throws<InvalidOperationException>(() =>
            CaptureDecisionBinding.ResolveSnapshot(
                Decision(CaptureDecisionBinding.IdentityOf(first)),
                [first, duplicate]));

        Assert.Contains("duplicate display ID", error.Message);
    }

    private static CaptureDecision Decision(CaptureDisplayIdentity display)
        => new("save", new CaptureSelection
        {
            Display = display,
            X = 10,
            Y = 20,
            Width = 100,
            Height = 80,
            Layers = new Layers<ImageSpace>()
        });

    private static DisplaySnapshot Snapshot(
        string id,
        int left,
        int top,
        int width,
        int height,
        double dpiScale)
        => new()
        {
            DisplayId = id,
            DeviceName = id,
            DisplayIndex = 0,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            DpiScale = dpiScale,
            PngData = []
        };
}
