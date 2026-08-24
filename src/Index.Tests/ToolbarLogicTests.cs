using Index.Annotation;
using Index.Toolbar;

namespace Index.Tests;

public sealed class ToolbarLogicTests
{
    private static readonly ToolbarContext Context = new(
        new AnnotationState(),
        ToolbarScope.Capture,
        _ => { });

    [Fact]
    public void Arrange_UsesTwoRowsBelowAnchor_WhenThereIsRoom()
    {
        var result = ToolbarLayout.Arrange(
            CreateTwoRowControls(),
            Context,
            new ToolbarRect(0, 0, 800, 600),
            new ToolbarRect(200, 100, 300, 200));

        Assert.True(result.IsBelowAnchor);
        Assert.Equal(ToolbarLayout.RowHeight * 2 + ToolbarLayout.InterRowGap, result.Height);
        Assert.Equal(2, result.RowFrames.Count);
        Assert.Equal(0, result.RowFrames[0].Y);
        Assert.Equal(ToolbarLayout.RowHeight + ToolbarLayout.InterRowGap, result.RowFrames[1].Y);
        Assert.Equal(300 + ToolbarLayout.AnchorGap, result.Y);
    }

    [Fact]
    public void Arrange_FlipsAboveAnchor_AndPlacesStyleRowClosestToAnchorEdge()
    {
        var result = ToolbarLayout.Arrange(
            CreateTwoRowControls(),
            Context,
            new ToolbarRect(0, 0, 800, 600),
            new ToolbarRect(200, 520, 300, 60));

        Assert.False(result.IsBelowAnchor);
        Assert.Equal(2, result.RowFrames.Count);
        Assert.Equal(ToolbarLayout.RowHeight + ToolbarLayout.InterRowGap, result.RowFrames[0].Y);
        Assert.Equal(0, result.RowFrames[1].Y);
        Assert.Equal(520 - ToolbarLayout.AnchorGap - result.Height, result.Y);
    }

    [Theory]
    [InlineData("third", 1, "first")]
    [InlineData("first", -1, "third")]
    [InlineData(null, 1, "first")]
    [InlineData(null, -1, "third")]
    [InlineData("missing", 1, "first")]
    public void FocusNavigator_CyclesAndChoosesDirectionalFallback(
        string? current,
        int offset,
        string expected)
    {
        string[] ids = ["first", "second", "third"];

        Assert.Equal(expected, ToolbarFocusNavigator.Next(ids, current, offset));
    }

    [Fact]
    public void CaptureDefaults_PutPinBeforeCopyCompleteAndCancel()
    {
        var registry = new ToolbarRegistry();
        BuiltinToolbarControls.RegisterCaptureDefaults(registry);

        var ids = registry.ControlsFor(Context).Select(control => control.Id).ToArray();

        Assert.Equal(
            [ToolbarCommandIds.Pin, ToolbarCommandIds.Copy, ToolbarCommandIds.Complete, ToolbarCommandIds.Cancel],
            ids);
    }

    [Fact]
    public void PinnedDefaults_ExposeCopySaveAndCloseOnly()
    {
        var registry = new ToolbarRegistry();
        BuiltinToolbarControls.RegisterPinnedDefaults(registry);
        var context = new ToolbarContext(new AnnotationState(), ToolbarScope.Pinned, _ => { });

        var ids = registry.ControlsFor(context).Select(control => control.Id).ToArray();

        Assert.Equal(
            [ToolbarCommandIds.Copy, ToolbarCommandIds.Save, ToolbarCommandIds.Close],
            ids);
    }

    [Fact]
    public void CaptureDefaults_UseEqualCompactActionSlots()
    {
        var registry = new ToolbarRegistry();
        BuiltinToolbarControls.RegisterCaptureDefaults(registry);

        var controls = registry.ControlsFor(Context);

        Assert.All(controls, control => Assert.False(control.ShowsLabel));
        Assert.All(controls, control =>
            Assert.Equal(ToolbarLayout.IconButtonWidth, control.PreferredWidth(Context)));
    }

    [Fact]
    public void ToolbarMetrics_MatchCompactTwoRowContract()
    {
        Assert.Equal(34, ToolbarLayout.RowHeight);
        Assert.Equal(30, ToolbarLayout.IconButtonWidth);
        Assert.Equal(22, ToolbarLayout.StyleButtonWidth);
        Assert.Equal(8, ToolbarLayout.InterRowGap);
        Assert.Equal(8, ToolbarLayout.AnchorGap);
    }

    [Fact]
    public void AnnotationDefaults_PutUnpinnedAvailableToolsInMoreMenu()
    {
        var registry = new ToolbarRegistry();
        AnnotationToolbarControls.RegisterCaptureAnnotationDefaults(registry);

        var more = Assert.IsType<MoreAnnotationToolsToolbarControl>(
            registry.ControlsFor(Context).Single(control =>
                control.Id == AnnotationToolbarControlIds.MoreTools));

        Assert.Equal(
            [AnnotationTool.Line, AnnotationTool.Dimension],
            more.Descriptors.Select(descriptor => descriptor.Tool));
        Assert.DoesNotContain(more.Descriptors, descriptor => descriptor.IsPinnedToBar);
        Assert.DoesNotContain(more.Descriptors, descriptor =>
            descriptor.Tool is AnnotationTool.Text or AnnotationTool.Pixelate or AnnotationTool.Crop);
    }

    [Fact]
    public void MoreToolsSelection_ActivatesToolAndRevealsItsToolbarControl()
    {
        var annotation = new AnnotationState();
        var context = new ToolbarContext(annotation, ToolbarScope.Capture, _ => { });
        var registry = new ToolbarRegistry();
        AnnotationToolbarControls.RegisterCaptureAnnotationDefaults(registry);
        var more = Assert.IsType<MoreAnnotationToolsToolbarControl>(
            registry.ControlsFor(context).Single(control =>
                control.Id == AnnotationToolbarControlIds.MoreTools));

        more.SelectTool(AnnotationTool.Line, context);

        Assert.Equal(AnnotationTool.Line, annotation.Tool);
        var line = registry.ControlsFor(context).Single(control =>
            control.Id == AnnotationToolbarControlIds.Tool(AnnotationTool.Line));
        Assert.True(line.IsSelected(context));
    }

    [Fact]
    public void AnnotationDefaults_CombineRectangleAndEllipseIntoOneShapeControl()
    {
        var registry = new ToolbarRegistry();
        AnnotationToolbarControls.RegisterCaptureAnnotationDefaults(registry);
        var controls = registry.ControlsFor(Context);

        Assert.Single(controls, control => control.Id == AnnotationToolbarControlIds.Shape);
        Assert.DoesNotContain(controls, control =>
            control.Id is "tool.rect" or "tool.ellipse");
    }

    [Fact]
    public void ShapeControl_SelectsBothVariantsAndReflectsActiveShape()
    {
        var annotation = new AnnotationState();
        var context = new ToolbarContext(annotation, ToolbarScope.Capture, _ => { });
        var shape = new ShapeAnnotationToolbarControl();

        shape.SelectTool(AnnotationTool.Ellipse, context);
        Assert.Equal(AnnotationTool.Ellipse, annotation.Tool);
        Assert.Equal(AnnotationTool.Ellipse, shape.ActiveTool(context));
        Assert.True(shape.IsSelected(context));

        shape.SelectTool(AnnotationTool.Rect, context);
        Assert.Equal(AnnotationTool.Rect, annotation.Tool);
        Assert.Equal(AnnotationTool.Rect, shape.ActiveTool(context));
    }

    private static IToolbarControl[] CreateTwoRowControls() =>
    [
        new CommandToolbarControl(
            "tool.rect", "R", "Rectangle", ToolbarGroup.Tools, 0, false, ToolbarScope.Capture),
        new CommandToolbarControl(
            "history.undo", "U", "Undo", ToolbarGroup.History, 0, false, ToolbarScope.Capture),
        new CommandToolbarControl(
            "style.color", "C", "Color", ToolbarGroup.Style, 0, false, ToolbarScope.Capture)
    ];
}
