using Ketcher.WinUI3.Core.Geometry;
using Xunit;

namespace Ketcher.WinUI3.Core.Tests;

public class ViewportTransformTests
{
    [Fact]
    public void ToScreen_IdentityTransform_ReturnsSameCoordinates()
    {
        var vp = new ViewportTransform();
        var doc = new Vector2(10, 20);

        var screen = vp.ToScreen(doc);

        Assert.Equal(10, screen.X, 4);
        Assert.Equal(20, screen.Y, 4);
    }

    [Fact]
    public void ToDocument_IdentityTransform_ReturnsSameCoordinates()
    {
        var vp = new ViewportTransform();
        var screen = new Vector2(10, 20);

        var doc = vp.ToDocument(screen);

        Assert.Equal(10, doc.X, 4);
        Assert.Equal(20, doc.Y, 4);
    }

    [Fact]
    public void ToScreen_WithScale_ScalesCoordinates()
    {
        var vp = new ViewportTransform();
        vp.Zoom(2.0, 0, 0);

        var screen = vp.ToScreen(new Vector2(10, 20));

        Assert.Equal(20, screen.X, 4);
        Assert.Equal(40, screen.Y, 4);
    }

    [Fact]
    public void ToDocument_WithScale_InverseScalesCoordinates()
    {
        var vp = new ViewportTransform();
        vp.Zoom(2.0, 0, 0);

        var doc = vp.ToDocument(new Vector2(20, 40));

        Assert.Equal(10, doc.X, 4);
        Assert.Equal(20, doc.Y, 4);
    }

    [Fact]
    public void Pan_ShiftsOffset()
    {
        var vp = new ViewportTransform();
        vp.Pan(100, 50);

        Assert.Equal(100, vp.OffsetX, 4);
        Assert.Equal(50, vp.OffsetY, 4);

        var screen = vp.ToScreen(Vector2.Zero);
        Assert.Equal(100, screen.X, 4);
        Assert.Equal(50, screen.Y, 4);
    }

    [Fact]
    public void Zoom_KeepsCenterPointStable()
    {
        var vp = new ViewportTransform();
        vp.Pan(100, 100);

        double cx = 200, cy = 200;
        var before = vp.ToDocument(new Vector2(cx, cy));

        vp.Zoom(2.0, cx, cy);

        var after = vp.ToDocument(new Vector2(cx, cy));

        Assert.Equal(before.X, after.X, 4);
        Assert.Equal(before.Y, after.Y, 4);
    }

    [Fact]
    public void Zoom_WithPan_KeepsCenterStable()
    {
        var vp = new ViewportTransform();
        vp.Pan(50, 30);

        double cx = 300, cy = 250;
        var before = vp.ToDocument(new Vector2(cx, cy));

        vp.Zoom(0.5, cx, cy);

        var after = vp.ToDocument(new Vector2(cx, cy));

        Assert.Equal(before.X, after.X, 4);
        Assert.Equal(before.Y, after.Y, 4);
    }

    [Fact]
    public void Zoom_ClampsToMinScale()
    {
        var vp = new ViewportTransform();
        vp.Zoom(0.001, 100, 100);
        vp.Zoom(0.001, 100, 100);
        vp.Zoom(0.001, 100, 100);

        Assert.True(vp.Scale >= 0.05);
    }

    [Fact]
    public void Zoom_ClampsToMaxScale()
    {
        var vp = new ViewportTransform();
        vp.Zoom(1000.0, 100, 100);
        vp.Zoom(1000.0, 100, 100);

        Assert.True(vp.Scale <= 50.0);
    }

    [Fact]
    public void FitToContent_CentersContent()
    {
        var vp = new ViewportTransform();
        var contentMin = new Vector2(-5, -5);
        var contentMax = new Vector2(5, 5);

        vp.FitToContent(contentMin, contentMax, 400, 300, margin: 0);

        // 内容中心 (0,0) 应映射到视口中心 (200, 150)
        var center = vp.ToScreen(Vector2.Zero);
        Assert.Equal(200, center.X, 1);
        Assert.Equal(150, center.Y, 1);
    }

    [Fact]
    public void FitToContent_ScalesToFit()
    {
        var vp = new ViewportTransform();
        var contentMin = new Vector2(0, 0);
        var contentMax = new Vector2(100, 50);

        vp.FitToContent(contentMin, contentMax, 400, 200, margin: 0);

        // 内容宽度 100 应填满视口宽度 400
        var topLeft = vp.ToScreen(contentMin);
        var bottomRight = vp.ToScreen(contentMax);
        double renderedWidth = bottomRight.X - topLeft.X;
        double renderedHeight = bottomRight.Y - topLeft.Y;

        Assert.Equal(400, renderedWidth, 1);
        Assert.Equal(200, renderedHeight, 1);
    }

    [Fact]
    public void FitToContent_WithMargin_LeavesSpace()
    {
        var vp = new ViewportTransform();
        var contentMin = new Vector2(0, 0);
        var contentMax = new Vector2(100, 100);

        vp.FitToContent(contentMin, contentMax, 400, 400, margin: 50);

        var topLeft = vp.ToScreen(contentMin);
        var bottomRight = vp.ToScreen(contentMax);

        // 左上角应在 margin 之后
        Assert.True(topLeft.X >= 50);
        Assert.True(topLeft.Y >= 50);
        // 右下角应在视口减去 margin 之前
        Assert.True(bottomRight.X <= 350);
        Assert.True(bottomRight.Y <= 350);
    }

    [Fact]
    public void FitToContent_SinglePoint_CentersIt()
    {
        var vp = new ViewportTransform();
        var point = new Vector2(10, 20);

        vp.FitToContent(point, point, 400, 300, margin: 0);

        var screen = vp.ToScreen(point);
        Assert.Equal(200, screen.X, 1);
        Assert.Equal(150, screen.Y, 1);
    }

    [Fact]
    public void Reset_RestoresIdentity()
    {
        var vp = new ViewportTransform();
        vp.Pan(100, 200);
        vp.Zoom(3.0, 50, 50);

        vp.Reset();

        Assert.Equal(1.0, vp.Scale, 4);
        Assert.Equal(0, vp.OffsetX, 4);
        Assert.Equal(0, vp.OffsetY, 4);
    }

    [Fact]
    public void GetContentBounds_ReturnsCorrectMinMax()
    {
        var points = new List<(double x, double y)>
        {
            (1, 2), (5, 3), (-2, 7), (0, 0)
        };

        var (min, max) = ViewportTransform.GetContentBounds(points);

        Assert.Equal(-2, min.X, 4);
        Assert.Equal(0, min.Y, 4);
        Assert.Equal(5, max.X, 4);
        Assert.Equal(7, max.Y, 4);
    }

    [Fact]
    public void GetContentBounds_EmptyList_ReturnsZero()
    {
        var (min, max) = ViewportTransform.GetContentBounds(new List<(double, double)>());

        Assert.Equal(0, min.X, 4);
        Assert.Equal(0, min.Y, 4);
        Assert.Equal(0, max.X, 4);
        Assert.Equal(0, max.Y, 4);
    }

    [Fact]
    public void RoundTrip_DocumentToScreenToDocument_PreservesPosition()
    {
        var vp = new ViewportTransform();
        vp.Pan(123.45, 67.89);
        vp.Zoom(1.7, 100, 100);

        var original = new Vector2(42, -17);
        var screen = vp.ToScreen(original);
        var restored = vp.ToDocument(screen);

        Assert.Equal(original.X, restored.X, 6);
        Assert.Equal(original.Y, restored.Y, 6);
    }
}
