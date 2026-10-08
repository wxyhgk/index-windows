using Index.Annotation;
using Index.Render;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.System;
using Windows.UI.Core;

namespace Index.UI.Editor;

/// <summary>
/// 可嵌入任意 WinUI 宿主的标注画布。所有图层坐标都以组件左上角为原点。
/// 背景图可选；不设置时保持透明，适合直接叠在已确认的截图选区上。
/// </summary>
public sealed class AnnotationCanvasView : UserControl, IDisposable
{
    private enum PointerOperation
    {
        None,
        Draw,
        Move,
        Resize
    }

    private const double HandleHitRadius = 10;

    private readonly SKXamlCanvas _canvas;
    private SKBitmap? _backgroundBitmap;
    private SKBitmap? _pixelatedBackgroundBitmap;
    private string _pixelateSignature = string.Empty;
    private PointerOperation _pointerOperation;
    private bool _isSubscribed;
    private bool _lastCanUndo;
    private bool _lastCanRedo;
    private bool _disposed;

    public AnnotationCanvasView()
        : this(new AnnotationState())
    {
    }

    public AnnotationCanvasView(AnnotationState state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _lastCanUndo = State.CanUndo;
        _lastCanRedo = State.CanRedo;

        _canvas = new SKXamlCanvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IgnorePixelScaling = true
        };

        Content = _canvas;
        IsTabStop = true;

        _canvas.PaintSurface += OnPaintSurface;
        _canvas.PointerPressed += OnPointerPressed;
        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerReleased += OnPointerReleased;
        _canvas.PointerCaptureLost += OnPointerCaptureLost;
        KeyDown += OnKeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        SubscribeToState();
    }

    /// <summary>画布使用的状态机，可供外部工具栏读写。</summary>
    public AnnotationState State { get; }

    /// <summary>当前标注工具；null 表示指针/选择模式。</summary>
    public AnnotationTool? Tool
    {
        get => State.Tool;
        set => State.Tool = value;
    }

    public bool CanUndo => State.CanUndo;
    public bool CanRedo => State.CanRedo;

    /// <summary>状态变化后触发，包括工具、选中、图层和历史变化。</summary>
    public event EventHandler? StateChanged;

    /// <summary>CanUndo 或 CanRedo 发生变化时触发。</summary>
    public event EventHandler? HistoryAvailabilityChanged;

    /// <summary>
    /// 设置可选底图。图像会缩放至组件局部边界；传 null 恢复透明画布。
    /// 数据会在调用期间解码，调用方无需保留字节数组。
    /// </summary>
    public void SetBackground(byte[]? pngData)
    {
        ThrowIfDisposed();

        SKBitmap? replacement = null;
        if (pngData is { Length: > 0 })
            replacement = SKBitmap.Decode(pngData)
                ?? throw new ArgumentException("无法解码标注画布背景图。", nameof(pngData));

        var old = _backgroundBitmap;
        _backgroundBitmap = replacement;
        old?.Dispose();
        _pixelatedBackgroundBitmap?.Dispose();
        _pixelatedBackgroundBitmap = null;
        _pixelateSignature = string.Empty;
        InvalidateCanvas();
    }

    public bool Undo() => State.Undo();

    public bool Redo() => State.Redo();

    public bool DeleteSelected() => State.DeleteSelected();

    public void InvalidateCanvas()
    {
        if (!_disposed)
            _canvas.Invalidate();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => SubscribeToState();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelPointerOperation();
        UnsubscribeFromState();
    }

    private void SubscribeToState()
    {
        if (_isSubscribed || _disposed) return;
        State.Changed += OnAnnotationStateChanged;
        _isSubscribed = true;
    }

    private void UnsubscribeFromState()
    {
        if (!_isSubscribed) return;
        State.Changed -= OnAnnotationStateChanged;
        _isSubscribed = false;
    }

    private void OnAnnotationStateChanged()
    {
        InvalidateCanvas();
        StateChanged?.Invoke(this, EventArgs.Empty);

        bool canUndo = State.CanUndo;
        bool canRedo = State.CanRedo;
        if (canUndo == _lastCanUndo && canRedo == _lastCanRedo) return;

        _lastCanUndo = canUndo;
        _lastCanRedo = canRedo;
        HistoryAvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (ResolvePreviewBackground() is { } previewBackground)
        {
            using var backgroundPaint = new SKPaint
            {
                IsAntialias = true
            };
            canvas.DrawBitmap(
                previewBackground,
                new SKRect(0, 0, e.Info.Width, e.Info.Height),
                backgroundPaint);
        }

        LayerRenderer.Render(
            State.DisplayLayers,
            canvas,
            State.LineWidth,
            State.FontSize,
            e.Info.Width,
            e.Info.Height);

        DrawCompositionGuides(canvas);

        if (State.SelectedId is not { } selectedId) return;
        if (State.Layers.FirstIndex(selectedId) is not int selectedIndex) return;

        LayerRenderer.RenderSelectionHandles(State.Layers.Elements[selectedIndex], canvas);
    }

    private SKBitmap? ResolvePreviewBackground()
    {
        if (_backgroundBitmap is null)
            return null;
        var pixelates = State.Layers.Elements
            .Where(layer => layer.Kind == LayerKind.Pixelate)
            .ToArray();
        string signature = string.Join(
            '|',
            pixelates.Select(layer =>
                $"{layer.Id:N}:{layer.Rect.X:R}:{layer.Rect.Y:R}:{layer.Rect.W:R}:{layer.Rect.H:R}:{layer.BlockScale:R}"));
        if (pixelates.Length == 0)
        {
            _pixelatedBackgroundBitmap?.Dispose();
            _pixelatedBackgroundBitmap = null;
            _pixelateSignature = string.Empty;
            return _backgroundBitmap;
        }
        if (_pixelatedBackgroundBitmap is not null
            && string.Equals(signature, _pixelateSignature, StringComparison.Ordinal))
        {
            return _pixelatedBackgroundBitmap;
        }

        _pixelatedBackgroundBitmap?.Dispose();
        _pixelatedBackgroundBitmap = LayerRenderer.ApplyPixelate(
            _backgroundBitmap,
            pixelates);
        _pixelateSignature = signature;
        return _pixelatedBackgroundBitmap;
    }

    private void DrawCompositionGuides(SKCanvas canvas)
    {
        var committedIds = State.Layers.Elements.Select(layer => layer.Id).ToHashSet();
        foreach (var layer in State.DisplayLayers)
        {
            if (layer.Kind == LayerKind.Crop)
            {
                ToolRegistry.DescriptorFor(LayerKind.Crop)?.Draw(
                    layer,
                    canvas,
                    layer.LineWidth,
                    layer.FontSize);
            }
            else if (layer.Kind == LayerKind.Pixelate && !committedIds.Contains(layer.Id))
            {
                var rect = layer.Rect.Standardized();
                using var placeholder = new SKPaint
                {
                    Color = new SKColor(128, 128, 128, 150),
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRect(
                    new SKRect(
                        (float)rect.MinX,
                        (float)rect.MinY,
                        (float)rect.MaxX,
                        (float)rect.MaxY),
                    placeholder);
            }
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_disposed || _pointerOperation != PointerOperation.None) return;

        var currentPoint = e.GetCurrentPoint(_canvas);
        if (!currentPoint.Properties.IsLeftButtonPressed) return;

        Focus(FocusState.Pointer);
        var point = ToPoint(currentPoint.Position);

        if (State.Tool is null)
        {
            if (TryBeginResize(point))
            {
                _pointerOperation = PointerOperation.Resize;
            }
            else if (State.LayerAt(point) is { } hitId)
            {
                State.Select(hitId);
                State.BeginMove(hitId, point);
                _pointerOperation = PointerOperation.Move;
            }
            else
            {
                State.Select(null);
                // 空白处交还给 Overlay，让宿主继续处理整个截图选区的移动。
                e.Handled = false;
                return;
            }
        }
        else if (State.BeginDraw(point))
        {
            _pointerOperation = PointerOperation.Draw;
        }

        if (_pointerOperation != PointerOperation.None)
            _canvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_disposed || _pointerOperation == PointerOperation.None) return;

        var point = ToPoint(e.GetCurrentPoint(_canvas).Position);
        switch (_pointerOperation)
        {
            case PointerOperation.Draw:
                State.UpdateDraw(point, IsShiftDown());
                break;
            case PointerOperation.Move:
                State.UpdateMove(point);
                break;
            case PointerOperation.Resize:
                State.UpdateResize(point);
                break;
        }
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_disposed || _pointerOperation == PointerOperation.None) return;

        CompletePointerOperation();
        _canvas.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_disposed || _pointerOperation == PointerOperation.None) return;
        CompletePointerOperation();
    }

    private bool TryBeginResize(PointF point)
    {
        if (State.SelectedId is not { } selectedId) return false;
        if (State.Layers.FirstIndex(selectedId) is not int selectedIndex) return false;

        var layer = State.Layers.Elements[selectedIndex];
        var descriptor = ToolRegistry.DescriptorFor(layer.Kind);
        if (descriptor is null) return false;

        foreach (var handle in descriptor.ResizeHandles)
        {
            var location = descriptor.HandleLocation(handle, layer);
            double dx = point.X - location.X;
            double dy = point.Y - location.Y;
            if ((dx * dx) + (dy * dy) > HandleHitRadius * HandleHitRadius) continue;

            State.BeginResize(selectedId, handle, point);
            return true;
        }
        return false;
    }

    private void CompletePointerOperation()
    {
        var operation = _pointerOperation;
        _pointerOperation = PointerOperation.None;

        switch (operation)
        {
            case PointerOperation.Draw:
                State.EndDraw();
                break;
            case PointerOperation.Move:
                State.EndMove();
                break;
            case PointerOperation.Resize:
                State.EndResize();
                break;
        }
    }

    private void CancelPointerOperation()
    {
        if (_pointerOperation == PointerOperation.None) return;
        CompletePointerOperation();
        _canvas.ReleasePointerCaptures();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool controlDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (controlDown && e.Key == VirtualKey.Z)
        {
            if (State.Undo()) e.Handled = true;
            return;
        }

        if (controlDown && e.Key == VirtualKey.Y)
        {
            if (State.Redo()) e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Delete && State.DeleteSelected())
            e.Handled = true;
    }

    private static PointF ToPoint(Windows.Foundation.Point point) => new(point.X, point.Y);

    private static bool IsShiftDown() => InputKeyboardSource
        .GetKeyStateForCurrentThread(VirtualKey.Shift)
        .HasFlag(CoreVirtualKeyStates.Down);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        CancelPointerOperation();
        _disposed = true;

        UnsubscribeFromState();
        _canvas.PaintSurface -= OnPaintSurface;
        _canvas.PointerPressed -= OnPointerPressed;
        _canvas.PointerMoved -= OnPointerMoved;
        _canvas.PointerReleased -= OnPointerReleased;
        _canvas.PointerCaptureLost -= OnPointerCaptureLost;
        KeyDown -= OnKeyDown;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;

        _backgroundBitmap?.Dispose();
        _backgroundBitmap = null;
        _pixelatedBackgroundBitmap?.Dispose();
        _pixelatedBackgroundBitmap = null;
    }
}
