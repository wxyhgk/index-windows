using Index.Navigation;

namespace Index.Tests;

public sealed class MainNavigationStateTests
{
    [Fact]
    public void DefaultsToLibraryShots()
    {
        var state = new MainNavigationState();

        Assert.Equal(MainNavigationPage.Library, state.Page);
        Assert.Equal(LibrarySection.Shots, state.Section);
        Assert.True(state.IsLibraryShots);
        Assert.Equal(0, state.Generation);
    }

    [Fact]
    public void PreviewKeepsCurrentLibrarySection()
    {
        var state = new MainNavigationState();
        state.ShowLibrary(LibrarySection.Clipboard);

        var generation = state.ShowPreview();

        Assert.Equal(MainNavigationPage.PreviewWorkspace, state.Page);
        Assert.Equal(LibrarySection.Clipboard, state.Section);
        Assert.False(state.IsLibraryShots);
        Assert.Equal(2, generation);
    }

    [Fact]
    public void LibraryPreviewIsStillTreatedAsLibraryShots()
    {
        var state = new MainNavigationState();

        state.ShowPreview();

        Assert.True(state.IsLibraryShots);
    }

    [Fact]
    public void EveryTransitionInvalidatesPreviousGeneration()
    {
        var state = new MainNavigationState();

        var settingsGeneration = state.ShowPage(MainNavigationPage.Settings);
        var libraryGeneration = state.ShowLibrary(LibrarySection.Shots);
        var refreshGeneration = state.Refresh();

        Assert.Equal(1, settingsGeneration);
        Assert.Equal(2, libraryGeneration);
        Assert.Equal(3, refreshGeneration);
    }

    [Theory]
    [InlineData(MainNavigationPage.Library)]
    [InlineData(MainNavigationPage.PreviewWorkspace)]
    public void GenericPageTransitionRejectsStatefulWorkspacePages(MainNavigationPage page)
    {
        var state = new MainNavigationState();

        Assert.Throws<ArgumentOutOfRangeException>(() => state.ShowPage(page));
    }
}
