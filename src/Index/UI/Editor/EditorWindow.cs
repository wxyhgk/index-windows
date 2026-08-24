using Index.Annotation;
using Index.Render;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using SkiaSharp.Views.WindowsUI;
using Windows.System;
using Windows.UI.Core;

namespace Index.UI.Editor;

/// <summary>
/// 标注编辑器窗口：截图 + 标注层 + 工具栏。
/// 对应 macOS 端 EditorView + BuiltinControls。
/// </summary>
public sealed class EditorWindow : Window
{
    private readonly AnnotationState _state = new();
    private readonly SKXamlCanvas _canvas;
    private readonly SKBitmap? _backgroundBitmap;
    private readonly StackPanel _toolbar;
    private readonly StackPanel _toolButtons;
    private readonly StackPanel _colorButtons;
    private readonly StackPanel _widthButtons;
    private Button _undoButton;
    private Button _redoButton;

    /// <summary>导出结果（PNG 字节 + 图层）。</summary>
    public event Action<byte[], Layers<ImageSpace>>? Completed;

    public EditorWindow(byte[] screenshotPng, int selectionX, int selectionY, int selectionW, int selectionH)
    {
        AppWindow.Title = "Index — 编辑";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Max(400, selectionW + 100),
            Math.Max(300, selectionH + 100)));

        // 加载底图
        _backgroundBitmap = SKBitmap.FromEncoded(screenshotPng);

        // 画布
        _canvas = new SKXamlCanvas
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x2D, 0x2D))
        };
        _canvas.PaintSurface += OnPaintSurface;

        // 布局：顶部工具栏 + 画布
        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Padding = new Windows.UI.Xaml.Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E))
        };
        Grid.SetRow(_toolbar, 0);

        _toolButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        _colorButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        _widthButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        BuildToolbar();

        _toolbar.Children.Add(_toolButtons);
        _toolbar.Children.Add(new Rectangle
        {
            Width = 1,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x44, 0x44, 0x44)),
            Margin = new Windows.UI.Xaml.Thickness(6, 4, 6, 4)
        });
        _toolbar.Children.Add(_colorButtons);
        _toolbar.Children.Add(new Rectangle
        {
            Width = 1,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x44, 0x44, 0x44)),
            Margin = new Windows.UI.Xaml.Thickness(6, 4, 6, 4)
        });
        _toolbar.Children.Add(_widthButtons);

        // 撤销/重做
        _undoButton = CreateSmallButton("↩");
        _undoButton.Click += (_, _) => { _state.Undo(); InvalidateCanvas(); };
        _redoButton = CreateSmallButton("↪");
        _redoButton.Click += (_, _) => { _state.Redo(); InvalidateCanvas(); };
        _toolbar.Children.Add(_undoButton);
        _toolbar.Children.Add(_redoButton);

        // 完成按钮
        var doneButton = new Button
        {
            Content = "✓ 完成",
            Margin = new Windows.UI.Xaml.Thickness(12, 0, 0, 0),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        doneButton.Click += OnDoneClicked;
        _toolbar.Children.Add(doneButton);

        Grid.SetRow(_canvas, 1);

        rootGrid.Children.Add(_toolbar);
        rootGrid.Children.Add(_canvas);
        Content = rootGrid;

        // 指针事件挂到画布
        _canvas.PointerPressed += OnPointerPressed;
        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerReleased += OnPointerReleased;
        _canvas.KeyDown += OnKeyDown;

        // 状态变更时刷新
        _state.Changed += InvalidateCanvas;

        Closed += OnWindowClosed;

        UpdateToolbarState();
    }

    private void BuildToolbar()
    {
        // 工具按钮
        var tools = new[]
        {
            (AnnotationTool.Rect, "▭"),
            (AnnotationTool.Ellipse, "◯"),
            (AnnotationTool.Arrow, "→"),
            (AnnotationTool.Line, "╱"),
            (AnnotationTool.Text, "A"),
            (AnnotationTool.Highlight, "▬"),
            (AnnotationTool.Pixelate, "▦"),
            (AnnotationTool.Counter, "①"),
        };

        foreach (var (tool, icon) in tools)
        {
            var btn = new Button
            {
                Content = icon,
                Width = 36,
                Height = 36,
                Margin = new Windows.UI.Xaml.Thickness(1, 0, 1, 0),
                FontSize = 16
            };
            var capturedTool = tool;
            btn.Click += (_, _) =>
            {
                _state.Tool = _state.Tool == capturedTool ? null : capturedTool;
                UpdateToolbarState();
                InvalidateCanvas();
            };
            btn.Tag = capturedTool;
            _toolButtons.Children.Add(btn);
        }

        // 颜色按钮
        for (int i = 0; i < AnnotationState.Palette.Length; i++)
        {
            var c = AnnotationState.Palette[i];
            var btn = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Windows.UI.Xaml.Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(
                        (byte)(c.A * 255),
                        (byte)(c.R * 255),
                        (byte)(c.G * 255),
                        (byte)(c.B * 255))),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x66, 0x66, 0x66)),
                BorderThickness = new Windows.UI.Xaml.Thickness(2)
            };
            int colorIdx = i;
            btn.Click += (_, _) =>
            {
                _state.SetStyleIndex(colorIdx, ToolStyleAxis.Color);
                _state.ApplyColorToSelection();
                InvalidateCanvas();
            };
            btn.Tag = colorIdx;
            _colorButtons.Children.Add(btn);
        }

        // 线宽按钮
        for (int i = 0; i < ToolStyle.WidthSteps.Length; i++)
        {
            double w = ToolStyle.WidthSteps[i];
            var btn = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Windows.UI.Xaml.Thickness(2, 0, 2, 0),
                Content = new Border
                {
                    Width = w,
                    Height = w,
                    CornerRadius = new CornerRadius(w / 2),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            };
            int widthIdx = i;
            btn.Click += (_, _) =>
            {
                _state.SetStyleIndex(widthIdx, ToolStyleAxis.Width);
                _state.ApplyStyleToSelection(ToolStyleAxis.Width);
                InvalidateCanvas();
            };
            btn.Tag = widthIdx;
            _widthButtons.Children.Add(btn);
        }
    }

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        // 画底图
        if (_backgroundBitmap != null)
        {
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(_backgroundBitmap, 0, 0, paint);
        }

        // 画标注层
        var layers = _state.DisplayLayers;
        LayerRenderer.Render(layers, canvas, _state.LineWidth, _state.FontSize);

        // 画选中控制点
        if (_state.SelectedId is { } selId)
        {
            int idx = _state.Layers.Elements.FindIndex(l => l.Id == selId);
            if (idx >= 0)
                LayerRenderer.RenderSelectionHandles(_state.Layers.Elements[idx], canvas);
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(_canvas).Position;
        var point = new PointF(pos.X, pos.Y);

        // 指针模式：选中 / 拖动
        if (_state.Tool is null)
        {
            // 先检查是否点在选中图层的控制点上
            if (_state.SelectedId is { } selId)
            {
                int idx = _state.Layers.Elements.FindIndex(l => l.Id == selId);
                if (idx >= 0)
                {
                    var layer = _state.Layers.Elements[idx];
                    var descriptor = ToolRegistry.DescriptorFor(layer.Kind);
                    if (descriptor != null)
                    {
                        foreach (var handle in descriptor.ResizeHandles)
                        {
                            var hPos = descriptor.HandleLocation(handle, layer);
                            double dx = point.X - hPos.X;
                            double dy = point.Y - hPos.Y;
                            if (Math.Sqrt(dx * dx + dy * dy) < 10)
                            {
                                _state.BeginResize(selId, handle, point);
                                return;
                            }
                        }
                    }
                }
            }

            // 命中图层
            var hitId = _state.LayerAt(point);
            if (hitId != null)
            {
                _state.Select(hitId);
                _state.BeginMove(hitId.Value, point);
            }
            else
            {
                _state.Select(null);
            }
            return;
        }

        // 工具模式：落笔
        _state.BeginDraw(point);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(_canvas).Position;
        var point = new PointF(pos.X, pos.Y);

        if (_state.ResizeId != null)
        {
            _state.UpdateResize(point);
            return;
        }
        if (_state.Tool is null && _state.SelectedId != null)
        {
            _state.UpdateMove(point);
            return;
        }
        _state.UpdateDraw(point, IsShiftDown());
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_state.ResizeId != null)
        {
            _state.EndResize();
            return;
        }
        if (_state.Tool is null && _state.SelectedId != null)
        {
            _state.EndMove();
            return;
        }
        _state.EndDraw();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
            Close();

        // Ctrl+Z / Ctrl+Y
        if (e.Key == Windows.System.VirtualKey.Z &&
            (e.KeyStatus.IsControlKeyDown))
        {
            _state.Undo();
            InvalidateCanvas();
            e.Handled = true;
        }
        if (e.Key == Windows.System.VirtualKey.Y &&
            (e.KeyStatus.IsControlKeyDown))
        {
            _state.Redo();
            InvalidateCanvas();
            e.Handled = true;
        }

        // Delete 删除选中
        if (e.Key == Windows.System.VirtualKey.Delete)
        {
            _state.DeleteSelected();
            InvalidateCanvas();
            e.Handled = true;
        }
    }

    private static bool IsShiftDown() => InputKeyboardSource
        .GetKeyStateForCurrentThread(VirtualKey.Shift)
        .HasFlag(CoreVirtualKeyStates.Down);

    private void OnDoneClicked(object sender, RoutedEventArgs e)
    {
        // 导出：渲染到图像像素空间
        int w = _backgroundBitmap?.Width ?? 800;
        int h = _backgroundBitmap?.Height ?? 600;

        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        var canvas = surface.Canvas;

        if (_backgroundBitmap != null)
        {
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(_backgroundBitmap, 0, 0, paint);
        }

        var exportedLayers = _state.ExportLayers(new LRect(0, 0, w, h), 1.0);
        LayerRenderer.Render(exportedLayers.Elements, canvas, _state.LineWidth, _state.FontSize);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        Completed?.Invoke(data.ToArray(), exportedLayers);
        Close();
    }

    private void InvalidateCanvas()
    {
        _canvas.Invalidate();
        UpdateToolbarState();
    }

    private void UpdateToolbarState()
    {
        _undoButton.IsEnabled = _state.CanUndo;
        _redoButton.IsEnabled = _state.CanRedo;

        // 高亮当前工具
        foreach (var child in _toolButtons.Children.OfType<Button>())
        {
            if (child.Tag is AnnotationTool tool)
            {
                bool active = _state.Tool == tool;
                child.Background = active
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x33, 0x66, 0xFF))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(0x00, 0, 0, 0));
            }
        }
    }

    private static Button CreateSmallButton(string content)
    {
        return new Button
        {
            Content = content,
            Width = 32,
            Height = 32,
            FontSize = 14
        };
    }

    public EditorWindow() : this(Array.Empty<byte>(), 0, 0, 800, 600) { }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _state.Changed -= InvalidateCanvas;
        _backgroundBitmap?.Dispose();
    }
}
