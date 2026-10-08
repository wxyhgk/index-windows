namespace Index.Navigation;

public enum MainNavigationPage
{
    Library,
    PreviewWorkspace,
    EditorWorkspace,
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

    public MainNavigationPage EditorReturnPage { get; private set; } = MainNavigationPage.Library;

    public int Generation { get; private set; }

    public bool IsLibraryShots =>
        (Page == MainNavigationPage.Library
         || Page == MainNavigationPage.PreviewWorkspace
            && PreviewReturnPage == MainNavigationPage.Library
         || Page == MainNavigationPage.EditorWorkspace
            && EditorReturnPage == MainNavigationPage.Library)
        && Section == LibrarySection.Shots;

    public bool IsAppsWorkspace =>
        Page == MainNavigationPage.Apps
        || Page == MainNavigationPage.PreviewWorkspace
           && PreviewReturnPage == MainNavigationPage.Apps
        || Page == MainNavigationPage.EditorWorkspace
           && EditorReturnPage == MainNavigationPage.Apps;

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

    public int ShowEditor()
    {
        if (Page != MainNavigationPage.EditorWorkspace)
            EditorReturnPage = Page;
        Page = MainNavigationPage.EditorWorkspace;
        return ++Generation;
    }

    public MainNavigationPage ReturnFromEditor()
    {
        if (Page != MainNavigationPage.EditorWorkspace)
            throw new InvalidOperationException("当前不在图片编辑工作区。");
        Page = EditorReturnPage;
        Generation++;
        return Page;
    }

    public int ShowPage(MainNavigationPage page)
    {
        if (page is MainNavigationPage.Library
            or MainNavigationPage.PreviewWorkspace
            or MainNavigationPage.EditorWorkspace)
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
