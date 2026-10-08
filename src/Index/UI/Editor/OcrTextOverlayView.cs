using Index.Ocr;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Index.UI.Editor;

/// <summary>
/// Lightweight word hit targets for screenshot OCR. The frozen screenshot remains the visual
/// source of truth; this layer only paints the current selection and routes pointer gestures.
/// </summary>
internal sealed class OcrTextOverlayView : Canvas
{
    private static readonly Brush TransparentHitBrush =
        new SolidColorBrush(Windows.UI.Color.FromArgb(0x01, 0, 0, 0));
    private static readonly Brush SelectedFillBrush =
        new SolidColorBrush(Windows.UI.Color.FromArgb(0x48, 0x2F, 0x7D, 0xFF));
    private static readonly Brush SelectedStrokeBrush =
        new SolidColorBrush(Windows.UI.Color.FromArgb(0xF0, 0x4B, 0x91, 0xFF));

    private readonly List<Rectangle> _wordTargets = [];
    private OcrTextSelection? _selection;
    private OcrTextResult? _result;
    private UIElement? _captureOwner;
    private Point _dragStart;
    private bool _isDragging;

    public event Action? SelectionChanged;

    public bool HasSelection => _selection?.SelectedIndices.Count > 0;
    public int SelectedWordCount => _selection?.SelectedIndices.Count ?? 0;
    public string SelectedText => _selection?.SelectedText ?? "";

    public OcrTextOverlayView(bool hitTargetsOnly = false)
    {
        Background = hitTargetsOnly ? null : TransparentHitBrush;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
    }

    public void Bind(OcrTextResult result, double displayWidth, double displayHeight)
    {
        ArgumentNullException.ThrowIfNull(result);
        Clear();

        _result = result;
        _selection = new OcrTextSelection(result.Words);
        Width = Math.Max(1, displayWidth);
        Height = Math.Max(1, displayHeight);

        foreach (var word in result.Words)
        {
            var target = new Rectangle
            {
                Fill = TransparentHitBrush,
                StrokeThickness = 1,
                RadiusX = 2,
                RadiusY = 2
            };
            _wordTargets.Add(target);
            Children.Add(target);
        }

        PositionWordTargets();

        IsHitTestVisible = result.Words.Count > 0;
        Visibility = Visibility.Visible;
    }

    public void ResizeDisplay(double displayWidth, double displayHeight)
    {
        Width = Math.Max(1, displayWidth);
        Height = Math.Max(1, displayHeight);
        PositionWordTargets();
        RefreshSelectionVisuals();
    }

    public void SelectAll()
    {
        if (_selection is null)
            return;

        _selection.SelectAll();
        RefreshSelectionVisuals();
        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (_selection is null)
            return;

        _selection.Clear();
        RefreshSelectionVisuals();
        SelectionChanged?.Invoke();
    }

    public void Clear()
    {
        if (_captureOwner is not null)
            _captureOwner.ReleasePointerCaptures();
        _captureOwner = null;
        _isDragging = false;

        _wordTargets.Clear();
        Children.Clear();
        _selection = null;
        _result = null;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement owner
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || _selection is null)
        {
            return;
        }

        _captureOwner = owner;
        _dragStart = e.GetCurrentPoint(this).Position;
        _isDragging = owner.CapturePointer(e.Pointer);
        SelectDisplayRange(_dragStart, _dragStart);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || _selection is null)
            return;

        var current = e.GetCurrentPoint(this).Position;
        SelectDisplayRange(_dragStart, current);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
            return;

        _captureOwner?.ReleasePointerCapture(e.Pointer);
        _captureOwner = null;
        _isDragging = false;
        SelectionChanged?.Invoke();
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _captureOwner = null;
        _isDragging = false;
    }

    private void SelectDisplayRange(Point anchor, Point focus)
    {
        if (_selection is null || _result is null)
            return;

        double scaleX = _result.PixelWidth / Math.Max(Width, 1);
        double scaleY = _result.PixelHeight / Math.Max(Height, 1);
        _selection.SelectReadingRange(
            anchor.X * scaleX,
            anchor.Y * scaleY,
            focus.X * scaleX,
            focus.Y * scaleY,
            tolerance: 3 * Math.Max(scaleX, scaleY));
        RefreshSelectionVisuals();
    }

    private Rect ToDisplay(OcrPixelRect pixelRect)
    {
        if (_result is null)
            return default;

        double scaleX = Width / Math.Max(_result.PixelWidth, 1);
        double scaleY = Height / Math.Max(_result.PixelHeight, 1);
        var normalized = pixelRect.Normalized();
        return new Rect(
            normalized.X * scaleX,
            normalized.Y * scaleY,
            normalized.Width * scaleX,
            normalized.Height * scaleY);
    }

    private void RefreshSelectionVisuals()
    {
        var selected = _selection?.SelectedIndices;
        for (int index = 0; index < _wordTargets.Count; index++)
        {
            bool isSelected = selected?.Contains(index) == true;
            _wordTargets[index].Fill = isSelected ? SelectedFillBrush : TransparentHitBrush;
            _wordTargets[index].Stroke = isSelected ? SelectedStrokeBrush : null;
        }
    }

    private void PositionWordTargets()
    {
        if (_result is null)
            return;

        for (int index = 0; index < _wordTargets.Count && index < _result.Words.Count; index++)
        {
            var frame = ToDisplay(_result.Words[index].Bounds);
            var target = _wordTargets[index];
            target.Width = Math.Max(1, frame.Width + 4);
            target.Height = Math.Max(1, frame.Height + 4);
            SetLeft(target, Math.Max(0, frame.X - 2));
            SetTop(target, Math.Max(0, frame.Y - 2));
        }
    }
}
