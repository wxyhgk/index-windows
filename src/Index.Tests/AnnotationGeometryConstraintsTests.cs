using Index.Annotation;

namespace Index.Tests;

public sealed class AnnotationGeometryConstraintsTests
{
    [Theory]
    [InlineData(AnnotationTool.Rect)]
    [InlineData(AnnotationTool.Ellipse)]
    public void BoxTools_UseLongestAxisForEqualSides(AnnotationTool tool)
    {
        var constrained = AnnotationGeometryConstraints.ConstrainEndpoint(
            tool,
            new PointF(20, 30),
            new PointF(80, 50));

        Assert.Equal(new PointF(80, 90), constrained);
    }

    [Fact]
    public void BoxConstraint_PreservesDragDirection()
    {
        var constrained = AnnotationGeometryConstraints.ConstrainEndpoint(
            AnnotationTool.Rect,
            new PointF(100, 100),
            new PointF(70, 20));

        Assert.Equal(new PointF(20, 20), constrained);
    }

    [Theory]
    [InlineData(AnnotationTool.Line)]
    [InlineData(AnnotationTool.Arrow)]
    public void LinearTools_SnapToNearestFortyFiveDegreesWithoutChangingLength(AnnotationTool tool)
    {
        var constrained = AnnotationGeometryConstraints.ConstrainEndpoint(
            tool,
            new PointF(10, 20),
            new PointF(50, 50));

        double component = 50 / Math.Sqrt(2);
        Assert.Equal(10 + component, constrained.X, 8);
        Assert.Equal(20 + component, constrained.Y, 8);
    }

    [Fact]
    public void State_CanReleaseConstraintDuringSameDrag()
    {
        var state = new AnnotationState { Tool = AnnotationTool.Rect };

        state.BeginDraw(new PointF(10, 20));
        state.UpdateDraw(new PointF(70, 40), constrainGeometry: true);
        state.UpdateDraw(new PointF(70, 40), constrainGeometry: false);
        state.EndDraw();

        Assert.Equal(new LRect(10, 20, 60, 20), Assert.Single(state.Layers.Elements).Rect);
    }
}
