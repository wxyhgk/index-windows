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
    public void AppsPreviewReturnsToApplicationsWorkspace()
    {
        var state = new MainNavigationState();
        state.ShowPage(MainNavigationPage.Apps);

        state.ShowPreview();

        Assert.False(state.IsLibraryShots);
        Assert.True(state.IsAppsWorkspace);
        Assert.Equal(MainNavigationPage.Apps, state.PreviewReturnPage);
        Assert.Equal(MainNavigationPage.Apps, state.ReturnFromPreview());
        Assert.Equal(MainNavigationPage.Apps, state.Page);
        Assert.True(state.IsAppsWorkspace);
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

    [Fact]
    public void EditorReturnsToItsSourceWorkspace()
    {
        var state = new MainNavigationState();
        state.ShowPage(MainNavigationPage.Apps);

        state.ShowEditor();

        Assert.Equal(MainNavigationPage.EditorWorkspace, state.Page);
        Assert.True(state.IsAppsWorkspace);
        Assert.Equal(MainNavigationPage.Apps, state.ReturnFromEditor());
    }

    [Theory]
    [InlineData(MainNavigationPage.Library)]
    [InlineData(MainNavigationPage.PreviewWorkspace)]
    [InlineData(MainNavigationPage.EditorWorkspace)]
    public void GenericPageTransitionRejectsStatefulWorkspacePages(MainNavigationPage page)
    {
        var state = new MainNavigationState();

        Assert.Throws<ArgumentOutOfRangeException>(() => state.ShowPage(page));
    }
}
