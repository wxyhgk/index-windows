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

    public MainNavigationPage PreviewReturnPage { get; private set; } = MainNavigationPage.Library;

    public int Generation { get; private set; }

    public bool IsLibraryShots =>
        (Page == MainNavigationPage.Library
         || Page == MainNavigationPage.PreviewWorkspace
            && PreviewReturnPage == MainNavigationPage.Library)
        && Section == LibrarySection.Shots;

    public bool IsAppsWorkspace =>
        Page == MainNavigationPage.Apps
        || Page == MainNavigationPage.PreviewWorkspace
           && PreviewReturnPage == MainNavigationPage.Apps;

    public int ShowLibrary(LibrarySection section)
    {
        Page = MainNavigationPage.Library;
        Section = section;
        return ++Generation;
    }

    public int ShowPreview()
    {
        if (Page != MainNavigationPage.PreviewWorkspace)
            PreviewReturnPage = Page;
        Page = MainNavigationPage.PreviewWorkspace;
        return ++Generation;
    }

    public MainNavigationPage ReturnFromPreview()
    {
        if (Page != MainNavigationPage.PreviewWorkspace)
            throw new InvalidOperationException("当前不在预览工作区。 ");
        Page = PreviewReturnPage;
        Generation++;
        return Page;
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
