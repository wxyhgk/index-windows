namespace Index.Navigation;

public enum MainNavigationPage
{
    Library,
    PreviewWorkspace,
    Collections,
    Apps,
    Settings,
    Search
}

public enum LibrarySection
{
    Shots,
    Recordings,
    Clipboard,
    Ai
}

public sealed class MainNavigationState
{
    public MainNavigationPage Page { get; private set; } = MainNavigationPage.Library;

    public LibrarySection Section { get; private set; } = LibrarySection.Shots;

    public int Generation { get; private set; }

    public bool IsLibraryShots =>
        Page is MainNavigationPage.Library or MainNavigationPage.PreviewWorkspace
        && Section == LibrarySection.Shots;

    public int ShowLibrary(LibrarySection section)
    {
        Page = MainNavigationPage.Library;
        Section = section;
        return ++Generation;
    }

    public int ShowPreview()
    {
        Page = MainNavigationPage.PreviewWorkspace;
        return ++Generation;
    }

    public int ShowPage(MainNavigationPage page)
    {
        if (page is MainNavigationPage.Library or MainNavigationPage.PreviewWorkspace)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                page,
                "Library and preview transitions require their dedicated methods.");
        }

        Page = page;
        return ++Generation;
    }

    public int Refresh() => ++Generation;
}
