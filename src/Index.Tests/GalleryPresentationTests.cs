using Index.Storage;
using Index.UI.Gallery;

namespace Index.Tests;

public sealed class GalleryPresentationTests
{
    [Fact]
    public void LayoutUsesStableWidthAndQuantizesColumns()
    {
        var result = GalleryLayout.Calculate(new GalleryLayoutRequest(
            StableContainerWidth: 1000,
            TargetCardWidth: 220,
            HorizontalInset: 0,
            Spacing: 14,
            ScrollbarReserve: 16,
            ColumnWidthQuantum: 4,
            MinimumColumnWidth: 150));

        Assert.Equal(4, result.ColumnCount);
        Assert.Equal(232, result.ColumnWidth);
        Assert.True(result.UsedWidth <= result.AvailableWidth);
    }

    [Fact]
    public void LayoutPreservesPixelAspectRatio()
    {
        Assert.Equal(100, GalleryLayout.ThumbnailHeightForPixels(200, 1600, 800));
    }

    [Fact]
    public void CardAppearanceSeparatesContentFromBadges()
    {
        var shot = Shot(windowTitle: "文档", appName: "编辑器");

        var appearance = CardAppearance.ForShot(
            shot,
            isFavorite: true,
            category: " 工作 ",
            isAnnotated: true);

        Assert.Equal("截图", appearance.HeaderLabel);
        Assert.Equal("文档", appearance.CaptionTitle);
        Assert.Contains("编辑器", appearance.CaptionSubtitle);
        Assert.Equal("工作", appearance.CategoryLabel);
        Assert.True(appearance.HasBadge(CardBadgeFlags.Favorite));
        Assert.True(appearance.HasBadge(CardBadgeFlags.Category));
        Assert.True(appearance.HasBadge(CardBadgeFlags.Annotated));
    }

    [Fact]
    public void BrowserShot_UsesWebsiteHostInCardSubtitle()
    {
        var shot = Shot("Index", "Microsoft Edge") with
        {
            SourceUrl = "https://example.com/docs/page"
        };

        Assert.Contains("example.com", CardAppearance.ForShot(shot).CaptionSubtitle);
    }

    private static ShotRecord Shot(string? windowTitle, string? appName) => new(
        1,
        new string('a', 64),
        new DateTimeOffset(2026, 8, 23, 10, 20, 0, TimeSpan.Zero),
        1920,
        1080,
        1.25,
        appName,
        "app.id",
        windowTitle,
        null,
        0,
        "Display",
        0,
        0,
        1920,
        1080,
        "png");
}
