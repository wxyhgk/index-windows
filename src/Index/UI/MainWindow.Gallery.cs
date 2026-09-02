using Index.Navigation;
using Index.Storage;
using Index.UI.Gallery;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Index.UI;

public sealed partial class MainWindow
{
    private async Task LoadShotGalleryAsync(int generation)
    {
        try
        {
            var countTask = _shotStore.GetCountAsync();
            var firstPageTask = _shotStore.GetPageAsync();
            await Task.WhenAll(countTask, firstPageTask);
            var totalCount = await countTask;
            var firstPage = await firstPageTask;
            var shots = firstPage.Items;
            if (generation != _navigation.Generation)
                return;

            var page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid { Margin = new Thickness(0, 16, 0, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock
            {
                Text = $"所有截图  ·  {totalCount:N0} 个项目",
                FontSize = 21,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = _theme.Text,
                VerticalAlignment = VerticalAlignment.Center
            });
            var refresh = MakeButton("刷新", compact: true);
            refresh.Click += (_, _) =>
            {
                var nextGeneration = _navigation.Refresh();
                _ = LoadShotGalleryAsync(nextGeneration);
            };
            Grid.SetColumn(refresh, 1);
            header.Children.Add(refresh);
            page.Children.Add(header);

            FrameworkElement body;
            ShotGalleryGridView? nextGallery = null;
            if (shots.Count == 0)
            {
                var empty = MakeCenteredMessage("还没有截图", "按 Ctrl + Shift + A 开始截图");
                var capture = MakeButton("开始截图");
                capture.Margin = new Thickness(0, 8, 0, 0);
                capture.Click += async (_, _) => await _coordinator.BeginCaptureAsync("gallery");
                empty.Children.Add(capture);
                body = empty;
            }
            else
            {
                var gallery = new ShotGalleryGridView(firstPage, _shotStore, _theme);
                nextGallery = gallery;
                var detail = new ShotDetailPane(
                    _shotStore,
                    new GalleryShotCommands(_shotStore, _libraryOrganization, DispatcherQueue),
                    _theme);
                gallery.SelectionChanged += detail.ShowShot;
                gallery.PreviewRequested += shot => OpenPreview(gallery, shot);
                detail.PreviewRequested += shot => OpenPreview(gallery, shot);
                detail.ShotDeleted += deletedShot =>
                {
                    var nextGeneration = _navigation.Refresh();
                    _ = LoadShotGalleryAsync(nextGeneration);
                };
                var split = new Grid { ColumnSpacing = 16 };
                split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                split.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                split.Children.Add(gallery);
                Grid.SetColumn(detail, 1);
                split.Children.Add(detail);
                body = split;
            }
            Grid.SetRow(body, 1);
            page.Children.Add(body);

            var searchAppearance = BuildGallerySearchAppearance();
            Grid.SetRow(searchAppearance, 2);
            page.Children.Add(searchAppearance);

            DisposeActiveGallery();
            _contentHost.ShowLibraryBody(page);
            _activeGallery = nextGallery;
        }
        catch (Exception error)
        {
            if (generation != _navigation.Generation)
                return;
            DisposeActiveGallery();
            var failed = MakeCenteredMessage("图库读取失败", error.Message);
            _contentHost.ShowLibraryBody(failed);
        }
    }

    private void ShowCollections()
    {
        PrepareForNavigation();
        Select(_topButtons, MainNavigationPage.Collections);
        var generation = _navigation.ShowPage(MainNavigationPage.Collections);
        _contentHost.ShowPage(MakeCenteredMessage("正在读取收藏…", ""));
        _ = LoadCollectionsAsync(generation);
    }

    private void OpenPreview(ShotGalleryGridView gallery, ShotRecord shot)
    {
        _navigation.ShowPreview();
        if (!ReferenceEquals(_previewGallery, gallery))
        {
            if (_previewGallery is not null)
                _previewGallery.SelectionChanged -= SyncPreviewSelection;
            _previewGallery = gallery;
            _previewGallery.SelectionChanged += SyncPreviewSelection;
        }

        if (_previewView is null)
        {
            _previewView = new ShotPreviewView(
                _shotStore,
                _shotAssets,
                _recognitionPlugins,
                _theme,
                shot);
            _previewView.NavigationRequested += NavigatePreview;
            _previewView.CloseRequested += ClosePreview;
        }
        else
        {
            _previewView.ShowShot(shot);
        }

        _contentHost.ShowOverlay(_previewView);
        _previewView.Focus(FocusState.Programmatic);
    }

    private FrameworkElement BuildGallerySearchAppearance()
    {
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new FontIcon
        {
            Glyph = "\uE721",
            FontSize = 14,
            Foreground = _theme.Muted,
            VerticalAlignment = VerticalAlignment.Center
        });
        var placeholder = new TextBlock
        {
            Text = "搜索 App、标题、网址或截图里的文字",
            FontSize = 13,
            Foreground = _theme.Muted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(placeholder, 1);
        content.Children.Add(placeholder);
        var shortcut = new Border
        {
            Background = _theme.Selected,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 3, 7, 3),
            Child = new TextBlock
            {
                Text = "Ctrl K",
                FontSize = 11,
                Foreground = _theme.Muted
            }
        };
        Grid.SetColumn(shortcut, 2);
        content.Children.Add(shortcut);
        return new Border
        {
            MaxWidth = 520,
            Height = 42,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(14, 0, 10, 0),
            Background = _theme.Card,
            BorderBrush = _theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(21),
            Child = content
        };
    }

    private void SyncPreviewSelection(ShotRecord shot)
        => _previewView?.ShowShot(shot);

    private async void NavigatePreview(int delta)
    {
        if (_previewGallery is not null)
            await _previewGallery.MoveSelectionAsync(delta);
    }

    private void ClosePreview()
    {
        var wasActive = _previewView is not null
            && _contentHost.IsOverlayVisible(_previewView);
        if (_previewView is not null)
        {
            _contentHost.HideOverlay(_previewView);
            _previewView.Deactivate();
        }
        if (_previewGallery is not null)
        {
            _previewGallery.SelectionChanged -= SyncPreviewSelection;
            _previewGallery = null;
        }
        if (!wasActive)
            return;

        var returnPage = _navigation.ReturnFromPreview();
        if (returnPage == MainNavigationPage.Library && _galleryRefreshPending)
        {
            _galleryRefreshPending = false;
            var generation = _navigation.Refresh();
            _ = LoadShotGalleryAsync(generation);
        }
    }

    private async Task LoadCollectionsAsync(int generation)
    {
        try
        {
            var favorites = await _libraryOrganization.GetFavoriteIdsAsync();
            var collections = await _libraryOrganization.GetCollectionsAsync();
            if (generation != _navigation.Generation) return;

            var root = new StackPanel { Spacing = 18 };
            root.Children.Add(new TextBlock
            {
                Text = "收藏",
                FontSize = 26,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = _theme.Text
            });

            var cards = new StackPanel { Spacing = 10 };
            cards.Children.Add(CollectionRow("快速收藏", $"{favorites.Count} 张截图", "\uE734"));
            foreach (var collection in collections)
            {
                var subtitle = $"{collection.ItemCount} 张截图";
                if (!string.IsNullOrWhiteSpace(collection.Note))
                    subtitle += $" · {collection.Note}";
                cards.Children.Add(CollectionRow(collection.Name, subtitle, "\uE8B7"));
            }
            if (collections.Count == 0)
            {
                cards.Children.Add(new TextBlock
                {
                    Text = "还没有专题集。数据库已经准备好，下一步接入创建和成员管理。",
                    FontSize = 13,
                    Foreground = _theme.Muted,
                    Margin = new Thickness(4, 8, 4, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            root.Children.Add(cards);

            _contentHost.ShowPage(new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            });
        }
        catch (Exception error)
        {
            if (generation != _navigation.Generation) return;
            _contentHost.ShowPage(MakeCenteredMessage("收藏读取失败", error.Message));
        }
    }

    private Border CollectionRow(string title, string subtitle, string glyph)
    {
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(12),
            Background = _theme.Selected,
            Child = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = glyph,
                FontSize = 18,
                Foreground = _theme.Text
            }
        });
        var labels = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 15,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = _theme.Text
                },
                new TextBlock { Text = subtitle, FontSize = 12, Foreground = _theme.Muted }
            }
        };
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);
        return new Border
        {
            Background = _theme.Card,
            BorderBrush = _theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = row
        };
    }

}
