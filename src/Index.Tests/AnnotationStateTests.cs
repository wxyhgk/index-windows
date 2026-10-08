using Index.Annotation;

namespace Index.Tests;

public sealed class AnnotationStateTests
{
    [Fact]
    public void EndDraw_CommitsDraggedLayer_AndUndoRemovesIt()
    {
        var state = new AnnotationState { Tool = AnnotationTool.Rect };

        Assert.True(state.BeginDraw(new PointF(10, 20)));
        Assert.True(state.UpdateDraw(new PointF(70, 80)));
        Assert.True(state.EndDraw());

        var layer = Assert.Single(state.Layers.Elements);
        Assert.Equal(new LRect(10, 20, 60, 60), layer.Rect);
        Assert.True(state.CanUndo);

        Assert.True(state.Undo());
        Assert.Empty(state.Layers.Elements);
        Assert.True(state.CanRedo);
    }

    [Fact]
    public void EndMove_RecordsMove_AndUndoRestoresOriginalRect()
    {
        var state = new AnnotationState { Tool = AnnotationTool.Rect };
        state.BeginDraw(new PointF(10, 20));
        state.UpdateDraw(new PointF(70, 80));
        state.EndDraw();

        var layer = Assert.Single(state.Layers.Elements);
        var originalRect = layer.Rect;

        state.Tool = null;
        state.BeginMove(layer.Id, new PointF(20, 30));
        Assert.True(state.UpdateMove(new PointF(45, 65)));
        Assert.True(state.EndMove());
        Assert.Equal(originalRect.OffsetBy(25, 35), state.Layers.Elements[0].Rect);

        Assert.True(state.Undo());
        Assert.Single(state.Layers.Elements);
        Assert.Equal(originalRect, state.Layers.Elements[0].Rect);
    }

    [Fact]
    public void ExportLayers_UsesIndependentAxisScales()
    {
        var layers = new Layers<CanvasSpace>();
        layers.Append(new Layer(
            LayerKind.Rect,
            new LRect(15, 25, 30, 40),
            LColor.Red,
            2,
            fontSize: 12));

        var exported = layers.Projected(new LRect(5, 5, 100, 100), 2, 3);

        var layer = Assert.Single(exported.Elements);
        Assert.Equal(new LRect(20, 60, 60, 120), layer.Rect);
        Assert.Equal(2 * Math.Sqrt(6), layer.LineWidth, 8);
        Assert.Equal(12 * Math.Sqrt(6), layer.FontSize, 8);
    }

    [Fact]
    public void SelectionGeometry_ClampsResizeAndMoveToCanvasBounds()
    {
        var resized = new LRect(-30, 20, 180, 120).IntersectedWithBounds(100, 80);
        Assert.Equal(new LRect(0, 20, 100, 60), resized);

        var moved = new LRect(70, 60, 50, 30).PositionedWithinBounds(100, 80);
        Assert.Equal(new LRect(50, 50, 50, 30), moved);
    }

    [Fact]
    public void ApplyStyleToSelection_ChangesWidth_AndUndoRestoresIt()
    {
        var state = new AnnotationState { Tool = AnnotationTool.Rect };
        state.BeginDraw(new PointF(10, 20));
        state.UpdateDraw(new PointF(70, 80));
        state.EndDraw();

        var layer = Assert.Single(state.Layers.Elements);
        Assert.Equal(4, layer.LineWidth);

        state.Tool = null;
        state.Select(layer.Id);
        Assert.True(state.SetStyleIndex(2, ToolStyleAxis.Width));
        Assert.True(state.ApplyStyleToSelection(ToolStyleAxis.Width));
        Assert.Equal(8, state.Layers.Elements[0].LineWidth);

        Assert.True(state.Undo());
        Assert.Equal(4, state.Layers.Elements[0].LineWidth);
    }

    [Fact]
    public void ApplyStyleToSelection_ChangesHighlightOpacityWithoutChangingRgb()
    {
        var state = new AnnotationState { Tool = AnnotationTool.Highlight };
        state.BeginDraw(new PointF(10, 20));
        state.UpdateDraw(new PointF(70, 80));
        state.EndDraw();

        var layer = Assert.Single(state.Layers.Elements);
        state.Tool = null;
        state.Select(layer.Id);
        Assert.True(state.SetStyleIndex(2, ToolStyleAxis.Opacity));
        Assert.True(state.ApplyStyleToSelection(ToolStyleAxis.Opacity));

        var changed = state.Layers.Elements[0];
        Assert.Equal(layer.Color.R, changed.Color.R);
        Assert.Equal(layer.Color.G, changed.Color.G);
        Assert.Equal(layer.Color.B, changed.Color.B);
        Assert.Equal(0.60, changed.Color.A, 8);
    }

    [Fact]
    public void SetText_CoalescesContinuousTypingIntoOneUndoOperation()
    {
        var source = new Layers<ImageSpace>();
        var textLayer = new Layer(
            LayerKind.Text,
            new LRect(10, 20, 0, 0),
            LColor.Red,
            2,
            text: "原文",
            fontSize: 18);
        source.Append(textLayer);
        var state = new AnnotationState();
        state.LoadImageLayers(source);

        Assert.True(state.SetText(textLayer.Id, "新"));
        Assert.True(state.SetText(textLayer.Id, "新文字"));
        Assert.Equal("新文字", Assert.Single(state.Layers.Elements).Text);

        Assert.True(state.Undo());
        Assert.Equal("原文", Assert.Single(state.Layers.Elements).Text);
        Assert.True(state.Redo());
        Assert.Equal("新文字", Assert.Single(state.Layers.Elements).Text);
    }

    [Fact]
    public void SetText_RejectsMissingAndNonTextLayersWithoutChangingHistory()
    {
        var state = new AnnotationState { Tool = AnnotationTool.Rect };
        state.BeginDraw(new PointF(10, 20));
        state.UpdateDraw(new PointF(70, 80));
        state.EndDraw();
        var rectangle = Assert.Single(state.Layers.Elements);
        state.History.Reset();

        Assert.False(state.SetText(Guid.NewGuid(), "missing"));
        Assert.False(state.SetText(rectangle.Id, "not text"));
        Assert.False(state.CanUndo);
        Assert.Equal(string.Empty, rectangle.Text);
    }

    [Fact]
    public void EmptyTextDraftIsDiscardedWithoutUndoingPreviousLayer()
    {
        var state = new AnnotationState { Tool = AnnotationTool.Rect };
        state.BeginDraw(new PointF(10, 10));
        state.UpdateDraw(new PointF(30, 30));
        state.EndDraw();
        var rectangle = Assert.Single(state.Layers.Elements);
        state.Tool = AnnotationTool.Text;
        state.BeginDraw(new PointF(40, 40));

        Assert.True(state.Undo());

        Assert.Equal(rectangle.Id, Assert.Single(state.Layers.Elements).Id);
        Assert.True(state.CanUndo);
    }

    [Fact]
    public void CompletedTextDraftIsOneUndoableAddTransaction()
    {
        var state = new AnnotationState { Tool = AnnotationTool.Text };
        state.BeginDraw(new PointF(20, 20));
        var draft = Assert.Single(state.Layers.Elements);
        state.SetText(draft.Id, "一段文字");

        state.EndTextEditing();
        Assert.True(state.Undo());
        Assert.Empty(state.Layers.Elements);
        Assert.True(state.Redo());
        Assert.Equal("一段文字", Assert.Single(state.Layers.Elements).Text);
    }

}
