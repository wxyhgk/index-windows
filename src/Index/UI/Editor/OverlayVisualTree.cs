using Index.Annotation;
using Index.UI.Toolbar;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Index.UI.Editor;

/// <summary>
/// Builds and exposes the capture overlay's visual tree. Interaction and capture state remain in
/// <see cref="OverlayWindow"/>; this component owns only stable view composition and styling.
/// </summary>
internal sealed class OverlayVisualTree
{
    public const double HandleSize = 6;

    public OverlayVisualTree(AnnotationState annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        Root = new Grid { Background = new SolidColorBrush(Colors.Black) };
        FrozenImage = new Image
        {
            // A DIP margin is fractional at 125%/150% scaling and resamples the screenshot.
            Stretch = Stretch.Fill,
            UseLayoutRounding = true
        };

        var dimCanvas = new Canvas();
        var dimBrush = new SolidColorBrush(
            Windows.UI.Color.FromArgb(0xA6, 0x00, 0x00, 0x00));
        DimTop = new Rectangle { Fill = dimBrush };
        DimBottom = new Rectangle { Fill = dimBrush };
        DimLeft = new Rectangle { Fill = dimBrush };
        DimRight = new Rectangle { Fill = dimBrush };
        dimCanvas.Children.Add(DimTop);
        dimCanvas.Children.Add(DimBottom);
        dimCanvas.Children.Add(DimLeft);
        dimCanvas.Children.Add(DimRight);

        var selectionCanvas = new Canvas();
        SelectionBorder = new Rectangle
        {
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 1.75,
            Fill = null,
            Visibility = Visibility.Collapsed
        };
        InitialFrame = new Border
        {
            BorderBrush = new SolidColorBrush(Colors.DodgerBlue),
            BorderThickness = new Thickness(2),
            Margin = new Thickness(2),
            IsHitTestVisible = false
        };

        HandlesCanvas = new Canvas { Visibility = Visibility.Collapsed };
        Handles = new Shape[8];
        for (int index = 0; index < Handles.Length; index++)
        {
            var handle = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = new SolidColorBrush(Colors.White),
                Stroke = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xB8, 0x24, 0x27, 0x2D)),
                StrokeThickness = 1,
                Visibility = Visibility.Collapsed
            };
            Handles[index] = handle;
            HandlesCanvas.Children.Add(handle);
        }

        SizeLabelText = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        };
        SizeLabel = new Border
        {
            Background = new SolidColorBrush(
                Windows.UI.Color.FromArgb(0xEE, 0x16, 0x19, 0x20)),
            BorderBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb(0xFF, 0x35, 0x3A, 0x46)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 3, 7, 3),
            Child = SizeLabelText,
            Visibility = Visibility.Collapsed
        };

        Toolbar = new ToolbarView { Visibility = Visibility.Collapsed };
        AnnotationCanvas = new AnnotationCanvasView(annotation)
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        LiveTextOverlay = new OcrTextOverlayView();
        selectionCanvas.Children.Add(AnnotationCanvas);
        selectionCanvas.Children.Add(LiveTextOverlay);
        selectionCanvas.Children.Add(SelectionBorder);
        selectionCanvas.Children.Add(SizeLabel);
        selectionCanvas.Children.Add(Toolbar);

        Root.Children.Add(FrozenImage);
        Root.Children.Add(dimCanvas);
        Root.Children.Add(selectionCanvas);
        Root.Children.Add(HandlesCanvas);
        Root.Children.Add(InitialFrame);
    }

    public Grid Root { get; }
    public Image FrozenImage { get; }
    public Rectangle DimTop { get; }
    public Rectangle DimBottom { get; }
    public Rectangle DimLeft { get; }
    public Rectangle DimRight { get; }
    public Rectangle SelectionBorder { get; }
    public Border InitialFrame { get; }
    public Canvas HandlesCanvas { get; }
    public Shape[] Handles { get; }
    public ToolbarView Toolbar { get; }
    public AnnotationCanvasView AnnotationCanvas { get; }
    public OcrTextOverlayView LiveTextOverlay { get; }
    public Border SizeLabel { get; }
    public TextBlock SizeLabelText { get; }
}
