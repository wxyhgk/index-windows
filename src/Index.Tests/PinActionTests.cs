using Index.Actions;
using Index.Annotation;
using Index.Capture;
using Index.Pin;

namespace Index.Tests;

public sealed class PinActionTests
{
    [Fact]
    public async Task PinAction_ForwardsTheOriginalNonDestructiveContext()
    {
        var presenter = new RecordingPresenter();
        var context = Context();

        await new PinAction(presenter).PerformAsync(context);

        Assert.Same(context, presenter.Context);
    }

    [Fact]
    public async Task CloseAction_DismissesPinnedHost()
    {
        var host = new RecordingHost();
        var context = Context(host);

        await new CloseCaptureAction().PerformAsync(context);

        Assert.True(host.WasDismissed);
    }

    [Fact]
    public void Descriptors_StayInTheirIntendedScopes()
    {
        var pin = new PinAction(new RecordingPresenter()).Descriptor;
        var close = new CloseCaptureAction().Descriptor;

        Assert.Equal(CaptureActionIds.Pin, pin.Id);
        Assert.Contains(CaptureActionScope.Capture, pin.Scopes);
        Assert.DoesNotContain(CaptureActionScope.Pinned, pin.Scopes);
        Assert.Equal(CaptureActionIds.Close, close.Id);
        Assert.Contains(CaptureActionScope.Pinned, close.Scopes);
    }

    private static CaptureContext Context(ICaptureActionHost? host = null)
        => new()
        {
            Artifact = new CaptureArtifact([1], new Layers<ImageSpace>()),
            Region = new CaptureRegion(10, 20, 30, 40),
            Host = host
        };

    private sealed class RecordingPresenter : IPinPresenter
    {
        public CaptureContext? Context { get; private set; }

        public ValueTask PresentAsync(CaptureContext context, CancellationToken cancellationToken = default)
        {
            Context = context;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHost : ICaptureActionHost
    {
        public bool WasDismissed { get; private set; }
        public void Dismiss() => WasDismissed = true;
    }
}
