using Index.Platform;

namespace Index.Tests;

public sealed class VirtualDisplayRecoveryJournalTests
{
    [Fact]
    public void MarkerTracksCaptureOwnershipAndCanBeCleared()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "Index.Tests",
            Guid.NewGuid().ToString("N"));
        string markerPath = Path.Combine(directory, "virtual-display.pending");
        try
        {
            var journal = new VirtualDisplayRecoveryJournal(markerPath);

            Assert.False(journal.IsPending);
            journal.MarkPending();
            Assert.True(journal.IsPending);
            Assert.Contains("pid=", File.ReadAllText(markerPath));

            journal.Clear();
            Assert.False(journal.IsPending);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
