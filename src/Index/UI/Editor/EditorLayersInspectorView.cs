using Index.Annotation;
using Index.UI.Gallery;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Index.UI.Editor;

/// <summary>
/// Reusable editor inspector for selecting, deleting and editing annotation layers.
/// It owns presentation only; every mutation goes through <see cref="AnnotationState"/>.
/// </summary>
internal sealed class EditorLayersInspectorView : UserControl, IDisposable
{
    private readonly AnnotationState _annotation;
    private readonly GalleryTheme _theme;
    private readonly ListView _layers = new();
    private readonly Border _textEditorPanel;
    private readonly TextBox _textEditor = new();
    private readonly Button _deleteButton;
    private bool _isReadOnly;
    private bool _updatingLayers;
    private bool _updatingText;
    private bool _disposed;
    private string _layerSignature = string.Empty;
    private Guid? _lastEditingTextId;

    public EditorLayersInspectorView(AnnotationState annotation, GalleryTheme theme)
    {
        _annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));

        _layers.SelectionMode = ListViewSelectionMode.Single;
        _layers.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _layers.SelectionChanged += OnLayerSelectionChanged;

        _textEditor.Header = "文字内容";
        _textEditor.PlaceholderText = "输入标注文字";
        _textEditor.AcceptsReturn = false;
        _textEditor.TextWrapping = TextWrapping.NoWrap;
        _textEditor.MinHeight = 36;
        _textEditor.TextChanged += OnTextChanged;
        _textEditor.GotFocus += OnTextEditorGotFocus;
        _textEditor.LostFocus += OnTextEditorLostFocus;
        _textEditor.KeyDown += OnTextEditorKeyDown;

        _textEditorPanel = new Border
        {
            Padding = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = _textEditor
        };

        _deleteButton = MakeDeleteButton();
        _deleteButton.Click += OnDeleteClicked;

        Content = BuildLayout();
        _annotation.Changed += OnAnnotationChanged;
        RefreshFromState();
    }

    /// <summary>
    /// Disables all mutation controls while keeping the layer list inspectable.
    /// </summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (_isReadOnly == value) return;
            _isReadOnly = value;
            RefreshEnabledState();
        }
    }

    /// <summary>True while keyboard editing belongs to the text box.</summary>
    public bool IsTextInputFocused => _textEditor.FocusState != FocusState.Unfocused;

    private FrameworkElement BuildLayout()
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        content.Children.Add(new TextBlock
        {
            Text = "图层",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _theme.Text,
            Margin = new Thickness(4, 2, 4, 8)
        });

        Grid.SetRow(_layers, 1);
        content.Children.Add(_layers);
        Grid.SetRow(_textEditorPanel, 2);
        content.Children.Add(_textEditorPanel);
        Grid.SetRow(_deleteButton, 3);
        content.Children.Add(_deleteButton);

        return new Border
        {
            Background = _theme.Card,
            BorderBrush = _theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = content
        };
    }

    private Button MakeDeleteButton()
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new FontIcon
                {
                    Glyph = "\uE74D",
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 14,
                    Foreground = _theme.Text
                },
                new TextBlock
                {
                    Text = "删除所选图层",
                    FontSize = 12,
                    Foreground = _theme.Text,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };

        return new Button
        {
            MinHeight = 34,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = _theme.Panel,
            BorderBrush = _theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Content = content
        };
    }

    private void OnAnnotationChanged()
    {
        if (_disposed) return;
        if (DispatcherQueue.HasThreadAccess)
        {
            RefreshFromState();
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed)
                RefreshFromState();
        });
    }

    private void RefreshFromState()
    {
        if (_disposed) return;

        string signature = string.Join(
            '|',
            _annotation.Layers.Elements.Select(
                layer => $"{layer.Id:N}:{layer.Kind}:{layer.Text}"));
        if (!string.Equals(signature, _layerSignature, StringComparison.Ordinal))
        {
            _layerSignature = signature;
            _updatingLayers = true;
            _layers.Items.Clear();
            foreach (var layer in _annotation.Layers.Elements.AsEnumerable().Reverse())
            {
                _layers.Items.Add(new ListViewItem
                {
                    Tag = layer.Id,
                    Content = new TextBlock
                    {
                        Text = LayerTitle(layer),
                        Foreground = _theme.Text,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                });
            }
            _updatingLayers = false;
        }

        Guid? effectiveSelection = _annotation.SelectedId ?? _annotation.EditingTextId;
        _updatingLayers = true;
        _layers.SelectedItem = _layers.Items
            .OfType<ListViewItem>()
            .FirstOrDefault(item => item.Tag is Guid id && id == effectiveSelection);
        _updatingLayers = false;

        Layer? selectedLayer = FindLayer(effectiveSelection);
        bool isText = selectedLayer?.Kind == LayerKind.Text;
        bool shouldFocusNewDraft = !_isReadOnly
            && _annotation.EditingTextId is { } editingId
            && editingId != _lastEditingTextId;
        _lastEditingTextId = _annotation.EditingTextId;
        _textEditorPanel.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        string displayedText = isText ? selectedLayer!.Text : string.Empty;
        if (!string.Equals(_textEditor.Text, displayedText, StringComparison.Ordinal))
        {
            _updatingText = true;
            _textEditor.Text = displayedText;
            _updatingText = false;
        }
        RefreshEnabledState();
        if (shouldFocusNewDraft)
        {
            _textEditor.Focus(FocusState.Programmatic);
            _textEditor.SelectionStart = _textEditor.Text.Length;
        }
    }

    private void RefreshEnabledState()
    {
        bool hasSelection = _annotation.SelectedId.HasValue || _annotation.EditingTextId.HasValue;
        _deleteButton.IsEnabled = !_isReadOnly && hasSelection;
        _textEditor.IsReadOnly = _isReadOnly;
        _layers.IsEnabled = true;
    }

    private Layer? FindLayer(Guid? id)
    {
        if (id is not { } value || _annotation.Layers.FirstIndex(value) is not int index)
            return null;
        return _annotation.Layers.Elements[index];
    }

    private void OnLayerSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_updatingLayers || _disposed) return;

        Guid? selectedId = _layers.SelectedItem is ListViewItem { Tag: Guid id }
            ? id
            : null;
        if (_annotation.EditingTextId != selectedId)
            _annotation.EndTextEditing();
        _annotation.Select(selectedId);
    }

    private void OnTextChanged(object sender, TextChangedEventArgs args)
    {
        if (_updatingText || _isReadOnly || _disposed) return;
        Guid? selectedId = _annotation.SelectedId ?? _annotation.EditingTextId;
        if (selectedId is { } id)
            _annotation.SetText(id, _textEditor.Text);
    }

    private void OnTextEditorGotFocus(object sender, RoutedEventArgs args)
    {
        if (_annotation.SelectedId is null && _annotation.EditingTextId is { } editingId)
            _annotation.Select(editingId);
        _annotation.History.BreakCoalescing();
    }

    private void OnTextEditorLostFocus(object sender, RoutedEventArgs args)
        => _annotation.History.BreakCoalescing();

    private void OnTextEditorKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_isReadOnly && args.Key != VirtualKey.Escape)
            return;
        bool controlDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (controlDown && args.Key == VirtualKey.Z)
        {
            _annotation.Undo();
            args.Handled = true;
            return;
        }

        if (controlDown && args.Key == VirtualKey.Y)
        {
            _annotation.Redo();
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.Delete)
        {
            DeleteTextForward();
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.Escape)
        {
            _layers.Focus(FocusState.Programmatic);
            args.Handled = true;
        }
    }

    private void DeleteTextForward()
    {
        if (_isReadOnly) return;

        int start = _textEditor.SelectionStart;
        int length = _textEditor.SelectionLength;
        if (length == 0 && start < _textEditor.Text.Length)
            length = 1;
        if (length <= 0) return;

        _textEditor.Text = _textEditor.Text.Remove(start, length);
        _textEditor.SelectionStart = start;
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs args)
    {
        if (_isReadOnly) return;

        Guid? selectedId = _annotation.SelectedId ?? _annotation.EditingTextId;
        if (selectedId is not { } id) return;

        _annotation.EndTextEditing();
        _annotation.Select(id);
        _annotation.DeleteSelected();
    }

    private static string LayerTitle(Layer layer) => layer.Kind switch
    {
        LayerKind.Rect => "矩形",
        LayerKind.Ellipse => "椭圆",
        LayerKind.Arrow => "箭头",
        LayerKind.Line => "直线",
        LayerKind.Text => string.IsNullOrWhiteSpace(layer.Text) ? "文字" : $"文字：{layer.Text}",
        LayerKind.Highlight => "高亮",
        LayerKind.Pixelate => "马赛克",
        LayerKind.Crop => "裁剪",
        LayerKind.Counter => "序号",
        LayerKind.Spotlight => "聚光灯",
        LayerKind.Dimension => "尺寸",
        _ => layer.Kind.ToString()
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _annotation.Changed -= OnAnnotationChanged;
        _layers.SelectionChanged -= OnLayerSelectionChanged;
        _textEditor.TextChanged -= OnTextChanged;
        _textEditor.GotFocus -= OnTextEditorGotFocus;
        _textEditor.LostFocus -= OnTextEditorLostFocus;
        _textEditor.KeyDown -= OnTextEditorKeyDown;
        _deleteButton.Click -= OnDeleteClicked;
    }
}
