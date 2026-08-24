using Index.Clipboard;

namespace Index.Tests;

public sealed class ClipboardReplaySuppressionTests
{
    [Fact]
    public void RepeatedMatchingNotificationsAreSuppressedDuringReplayWindow()
    {
        var suppression = new ClipboardReplaySuppression();
        var hash = ClipboardContentHash.FromText("history item");

        suppression.Mark(hash);

        Assert.True(suppression.ShouldSuppress(hash));
        Assert.True(suppression.ShouldSuppress(hash));
    }

    [Fact]
    public void DifferentExternalContentIsNotSuppressed()
    {
        var suppression = new ClipboardReplaySuppression();
        suppression.Mark(ClipboardContentHash.FromText("history item"));

        Assert.False(suppression.ShouldSuppress(ClipboardContentHash.FromText("external copy")));
    }

    [Fact]
    public void FileHashesAreIndependentOfClipboardEnumerationOrder()
    {
        var first = ClipboardContentHash.FromFiles([@"C:\a.txt", @"C:\b.txt"]);
        var second = ClipboardContentHash.FromFiles([@"C:\b.txt", @"C:\a.txt"]);

        Assert.Equal(first, second);
    }
}
