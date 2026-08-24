using Index.Clipboard;

namespace Index.Tests;

public sealed class ClipboardPopupViewModelTests
{
    [Fact]
    public void FiltersByKindAndSearchesSourceApplication()
    {
        var model = Model();
        model.SetKindFilter(ClipboardItemKind.Text);
        model.SetQuery("terminal");

        var item = Assert.Single(model.VisibleItems);
        Assert.Equal(2, item.Id);
    }

    [Fact]
    public void SelectionWrapsAndClampsAfterDelete()
    {
        var model = Model();
        model.MoveSelection(-1);
        Assert.Equal(3, model.SelectedItem?.Id);

        model.Delete(3);
        Assert.Equal(2, model.SelectedItem?.Id);
    }

    private static ClipboardPopupViewModel Model()
    {
        var model = new ClipboardPopupViewModel();
        model.ReplaceItems(new[]
        {
            new ClipboardHistoryItem(1, ClipboardItemKind.Image, DateTimeOffset.Parse("2026-08-23T10:00:00Z"), "图", "图片", null, "Figma"),
            new ClipboardHistoryItem(2, ClipboardItemKind.Text, DateTimeOffset.Parse("2026-08-23T09:00:00Z"), "命令", "dotnet test", "dotnet test", "Windows Terminal"),
            new ClipboardHistoryItem(3, ClipboardItemKind.File, DateTimeOffset.Parse("2026-08-23T08:00:00Z"), "文档", "1 个文件", null, "Explorer")
        });
        return model;
    }
}
