using Index.Capture;

namespace Index.Tests;

public sealed class SelectionControllerTests
{
    private static readonly SelectionRect Bounds = new(0, 0, 100, 80);
    private static readonly SelectionRect Initial = new(20, 20, 40, 30);

    [Fact]
    public void Create_BeginUpdateEnd_NormalizesAndCommits()
    {
        var controller = NewController();

        Assert.True(controller.BeginCreate(new SelectionPoint(70, 60)));
        Assert.Equal(SelectionInteraction.Create, controller.Interaction);
        Assert.True(controller.Update(new SelectionPoint(10, 15)));
        Assert.Equal(new SelectionRect(10, 15, 60, 45), controller.Selection);
        Assert.True(controller.End());

        Assert.Equal(SelectionInteraction.None, controller.Interaction);
        Assert.Equal(new SelectionRect(10, 15, 60, 45), controller.Selection);
    }

    [Fact]
    public void Create_ClampsToCanvasBounds()
    {
        var controller = NewController();

        controller.BeginCreate(new SelectionPoint(-50, -40));
        controller.Update(new SelectionPoint(140, 120));

        Assert.Equal(Bounds, controller.Selection);
        Assert.True(controller.End());
    }

    [Fact]
    public void Create_BelowMinimumRollsBackPreviousSelection()
    {
        var controller = NewController(Initial);

        controller.BeginCreate(new SelectionPoint(5, 5));
        controller.Update(new SelectionPoint(10, 9));

        Assert.False(controller.End());
        Assert.Equal(Initial, controller.Selection);
    }

    [Fact]
    public void Move_PreservesSizeAndStaysInsideCanvas()
    {
        var controller = NewController(Initial);

        Assert.True(controller.BeginMove(new SelectionPoint(30, 30)));
        controller.Update(new SelectionPoint(200, 200));
        Assert.Equal(new SelectionRect(60, 50, 40, 30), controller.Selection);
        Assert.True(controller.End());
    }

    [Theory]
    [MemberData(nameof(ResizeCases))]
    public void Resize_AllEightHandles(
        SelectionResizeHandle handle,
        SelectionPoint start,
        SelectionPoint end,
        SelectionRect expected)
    {
        var controller = NewController(Initial);

        Assert.True(controller.BeginResize(handle, start));
        Assert.True(controller.Update(end));
        Assert.Equal(expected, controller.Selection);
        Assert.True(controller.End());
    }

    public static TheoryData<SelectionResizeHandle, SelectionPoint, SelectionPoint, SelectionRect> ResizeCases => new()
    {
        { SelectionResizeHandle.TopLeft, new(20, 20), new(10, 12), new(10, 12, 50, 38) },
        { SelectionResizeHandle.Top, new(40, 20), new(40, 10), new(20, 10, 40, 40) },
        { SelectionResizeHandle.TopRight, new(60, 20), new(70, 10), new(20, 10, 50, 40) },
        { SelectionResizeHandle.Right, new(60, 35), new(75, 35), new(20, 20, 55, 30) },
        { SelectionResizeHandle.BottomRight, new(60, 50), new(75, 65), new(20, 20, 55, 45) },
        { SelectionResizeHandle.Bottom, new(40, 50), new(40, 65), new(20, 20, 40, 45) },
        { SelectionResizeHandle.BottomLeft, new(20, 50), new(10, 65), new(10, 20, 50, 45) },
        { SelectionResizeHandle.Left, new(20, 35), new(10, 35), new(10, 20, 50, 30) }
    };

    [Fact]
    public void Resize_EnforcesMinimumSizeAndCanvasBounds()
    {
        var controller = NewController(Initial);

        controller.BeginResize(SelectionResizeHandle.TopLeft, new SelectionPoint(20, 20));
        controller.Update(new SelectionPoint(-100, -100));
        Assert.Equal(new SelectionRect(0, 0, 60, 50), controller.Selection);
        controller.End();

        controller.BeginResize(SelectionResizeHandle.BottomRight, new SelectionPoint(60, 50));
        controller.Update(new SelectionPoint(500, 500));
        Assert.Equal(Bounds, controller.Selection);
        controller.End();

        controller.BeginResize(SelectionResizeHandle.Left, new SelectionPoint(0, 40));
        controller.Update(new SelectionPoint(500, 40));
        Assert.Equal(new SelectionRect(92, 0, 8, 80), controller.Selection);
    }

    [Theory]
    [InlineData(SelectionInteraction.Create)]
    [InlineData(SelectionInteraction.Move)]
    [InlineData(SelectionInteraction.Resize)]
    public void Cancel_RestoresExactStartingSelection(SelectionInteraction interaction)
    {
        var controller = NewController(Initial);

        switch (interaction)
        {
            case SelectionInteraction.Create:
                controller.BeginCreate(new SelectionPoint(0, 0));
                break;
            case SelectionInteraction.Move:
                controller.BeginMove(new SelectionPoint(30, 30));
                break;
            case SelectionInteraction.Resize:
                controller.BeginResize(SelectionResizeHandle.BottomRight, new SelectionPoint(60, 50));
                break;
        }
        controller.Update(new SelectionPoint(90, 70));

        Assert.True(controller.Cancel());
        Assert.Equal(Initial, controller.Selection);
        Assert.Equal(SelectionInteraction.None, controller.Interaction);
        Assert.False(controller.Cancel());
    }

    private static SelectionController NewController(SelectionRect? initial = null) =>
        new(Bounds, minimumWidth: 8, minimumHeight: 6, initial);
}
