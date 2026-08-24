using Ketcher.WinUI3.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace Ketcher.WinUI3.Demo;

/// <summary>
/// Demo 主窗口：加载 MolGrapher SDF 并展示分子结构。
/// 不承载业务逻辑，仅用于验证渲染和交互。
/// </summary>
public sealed class MainWindow : Window
{
    private const string SampleSdf =
        "Demo Molecule\n" +
        "Ketcher.WinUI3\n" +
        "  3  2  0\n" +
        "  0  0  0\n" +
        "    0.0000    0.8660    0.0000  C  0  0  0  0  0  0\n" +
        "    1.5000    0.8660    0.0000  C  0  0  0  0  0  0\n" +
        "    1.5000   -0.8660    0.0000  O  0  0  0  0  0  0\n" +
        "  1  2  2  0\n" +
        "  2  3  1  0\n" +
        "$$$$\n" +
        "$$$$\n";

    private readonly MoleculeCanvasControl _moleculeCanvas;
    private readonly TextBlock _statusText;

    public MainWindow()
    {
        Title = "Ketcher WinUI3 Demo";

        var rootGrid = new Grid();

        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 工具栏
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Padding = new Thickness(8)
        };
        Grid.SetRow(toolbar, 0);

        // 工具按钮
        var btnSelect = new Button { Content = "Select", Width = 60, Margin = new Thickness(4, 0, 4, 0) };
        btnSelect.Click += (_, _) => SetTool(MoleculeTool.Select);
        toolbar.Children.Add(btnSelect);

        var btnAddAtom = new Button { Content = "Atom", Width = 55, Margin = new Thickness(4, 0, 4, 0) };
        btnAddAtom.Click += (_, _) => SetTool(MoleculeTool.AddAtom);
        toolbar.Children.Add(btnAddAtom);

        var btnAddBond = new Button { Content = "Bond", Width = 55, Margin = new Thickness(4, 0, 4, 0) };
        btnAddBond.Click += (_, _) => SetTool(MoleculeTool.AddBond);
        toolbar.Children.Add(btnAddBond);

        var btnAddRing = new Button { Content = "Ring", Width = 55, Margin = new Thickness(4, 0, 4, 0) };
        btnAddRing.Click += (_, _) => SetTool(MoleculeTool.AddRing);
        toolbar.Children.Add(btnAddRing);

        var btnDelete = new Button { Content = "Del", Width = 45, Margin = new Thickness(4, 0, 4, 0) };
        btnDelete.Click += (_, _) => SetTool(MoleculeTool.Delete);
        toolbar.Children.Add(btnDelete);

        // 元素选择
        var elementPicker = new ComboBox
        {
            Width = 60,
            Margin = new Thickness(8, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var el in new[] { "C", "N", "O", "S", "F", "Cl", "Br", "I", "P", "H" })
            elementPicker.Items.Add(new ComboBoxItem { Content = el });
        elementPicker.SelectedIndex = 0;
        elementPicker.SelectionChanged += (_, _) =>
        {
            if (elementPicker.SelectedItem is ComboBoxItem { Content: string s })
                _moleculeCanvas!.CurrentElement = s;
        };
        toolbar.Children.Add(elementPicker);

        // 键级选择
        var bondOrderPicker = new ComboBox
        {
            Width = 45,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var order in new[] { "1", "2", "3" })
            bondOrderPicker.Items.Add(new ComboBoxItem { Content = order });
        bondOrderPicker.SelectedIndex = 0;
        bondOrderPicker.SelectionChanged += (_, _) =>
        {
            if (bondOrderPicker.SelectedItem is ComboBoxItem { Content: string s })
                _moleculeCanvas!.CurrentBondOrder = int.Parse(s);
        };
        toolbar.Children.Add(bondOrderPicker);

        // 环模板选择
        var ringPicker = new ComboBox
        {
            Width = 90,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var ring in Ketcher.WinUI3.Core.Chemistry.RingTemplate.All)
            ringPicker.Items.Add(new ComboBoxItem { Content = ring.Name, Tag = ring });
        ringPicker.SelectedIndex = 0;
        ringPicker.SelectionChanged += (_, _) =>
        {
            if (ringPicker.SelectedItem is ComboBoxItem { Tag: Ketcher.WinUI3.Core.Chemistry.RingTemplate r })
                _moleculeCanvas!.CurrentRingTemplate = r;
        };
        toolbar.Children.Add(ringPicker);

        // 撤销/重做
        var btnUndo = new Button { Content = "↶", Width = 40, Margin = new Thickness(8, 0, 4, 0) };
        btnUndo.Click += OnUndoClick;
        toolbar.Children.Add(btnUndo);

        var btnRedo = new Button { Content = "↷", Width = 40, Margin = new Thickness(4, 0, 4, 0) };
        btnRedo.Click += OnRedoClick;
        toolbar.Children.Add(btnRedo);

        // 视图
        var btnFit = new Button { Content = "Fit", Width = 50, Margin = new Thickness(8, 0, 4, 0) };
        btnFit.Click += OnFitClick;
        toolbar.Children.Add(btnFit);

        var btnZoomIn = new Button { Content = "+", Width = 35, Margin = new Thickness(4, 0, 4, 0) };
        btnZoomIn.Click += OnZoomInClick;
        toolbar.Children.Add(btnZoomIn);

        var btnZoomOut = new Button { Content = "−", Width = 35, Margin = new Thickness(4, 0, 4, 0) };
        btnZoomOut.Click += OnZoomOutClick;
        toolbar.Children.Add(btnZoomOut);

        var btnNew = new Button { Content = "New", Width = 50, Margin = new Thickness(8, 0, 4, 0) };
        btnNew.Click += OnNewClick;
        toolbar.Children.Add(btnNew);

        _statusText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            Text = "Ready"
        };
        toolbar.Children.Add(_statusText);

        rootGrid.Children.Add(toolbar);

        // 分子画布
        _moleculeCanvas = new MoleculeCanvasControl();
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8, 0, 8, 8),
            Child = _moleculeCanvas
        };
        Grid.SetRow(border, 1);
        rootGrid.Children.Add(border);

        Content = rootGrid;

        // 设置窗口大小
        AppWindow.Resize(new SizeInt32(900, 700));

        Activated += (_, _) =>
        {
            _moleculeCanvas.LoadSdf(SampleSdf);
            _statusText.Text = "Loaded sample molecule (3 atoms, 2 bonds)";
        };
    }

    private void OnFitClick(object sender, RoutedEventArgs e)
    {
        _moleculeCanvas.FitToWindowPublic();
        _statusText.Text = "Fitted to window";
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e)
    {
        _moleculeCanvas.ZoomIn();
        _statusText.Text = "Zoomed in";
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs e)
    {
        _moleculeCanvas.ZoomOut();
        _statusText.Text = "Zoomed out";
    }

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        _moleculeCanvas.NewDocument();
        _statusText.Text = "New empty document";
    }

    private void SetTool(MoleculeTool tool)
    {
        _moleculeCanvas.Tool = tool;
        _statusText.Text = $"Tool: {tool}";
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        _moleculeCanvas.Undo();
        _statusText.Text = "Undo";
    }

    private void OnRedoClick(object sender, RoutedEventArgs e)
    {
        _moleculeCanvas.Redo();
        _statusText.Text = "Redo";
    }
}
