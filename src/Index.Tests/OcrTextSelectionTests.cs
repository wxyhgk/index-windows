using Index.Ocr;

namespace Index.Tests;

public sealed class OcrTextSelectionTests
{
    private static readonly OcrTextWord[] Words =
    [
        new("world", new OcrPixelRect(60, 10, 45, 18), 0, 1),
        new("second", new OcrPixelRect(10, 40, 52, 18), 1, 0),
        new("hello", new OcrPixelRect(10, 10, 42, 18), 0, 0)
    ];

    [Fact]
    public void DragSelectsIntersectingWordsAndPreservesReadingOrder()
    {
        var selection = new OcrTextSelection(Words);

        selection.Select(new OcrPixelRect(5, 5, 110, 30));

        Assert.Equal("hello world", selection.SelectedText);
        Assert.Equal(2, selection.SelectedIndices.Count);
    }

    [Fact]
    public void ClickSelectsSmallestContainingWord()
    {
        var words = new[]
        {
            new OcrTextWord("large", new OcrPixelRect(0, 0, 100, 50), 0, 0),
            new OcrTextWord("small", new OcrPixelRect(10, 10, 20, 10), 0, 1)
        };
        var selection = new OcrTextSelection(words);

        selection.Select(new OcrPixelRect(15, 15, 0, 0));

        Assert.Equal("small", selection.SelectedText);
    }

    [Fact]
    public void HitTestAllowsDisplayScaledToleranceWithoutChangingSelection()
    {
        var selection = new OcrTextSelection(Words);

        int hit = selection.HitTest(106, 19, tolerance: 2);

        Assert.Equal(0, hit);
        Assert.Empty(selection.SelectedIndices);
    }

    [Fact]
    public void SelectAllUsesLineBreaksBetweenRecognizedLines()
    {
        var selection = new OcrTextSelection(Words);

        selection.SelectAll();

        Assert.Equal($"hello world{Environment.NewLine}second", selection.SelectedText);
    }

    [Fact]
    public void NormalizedRectangleSupportsReverseDrag()
    {
        var selection = new OcrTextSelection(Words);

        selection.Select(new OcrPixelRect(110, 35, -105, -30));

        Assert.Equal("hello world", selection.SelectedText);
    }

    [Fact]
    public void VerticalReadingRangeIncludesRightSideOfIntermediateLine()
    {
        var selection = new OcrTextSelection(Words);

        selection.SelectReadingRange(15, 15, 15, 50);

        Assert.Equal($"hello world{Environment.NewLine}second", selection.SelectedText);
        Assert.Equal(3, selection.SelectedIndices.Count);
    }

    [Fact]
    public void ReadingRangeSupportsReverseDrag()
    {
        var selection = new OcrTextSelection(Words);

        selection.SelectReadingRange(15, 50, 15, 15);

        Assert.Equal($"hello world{Environment.NewLine}second", selection.SelectedText);
    }

    [Fact]
    public void ReadingRangeResolvesWhitespaceToNearestWordOnClosestLine()
    {
        var selection = new OcrTextSelection(Words);

        selection.SelectReadingRange(15, 15, 130, 18);

        Assert.Equal("hello world", selection.SelectedText);
    }

    [Fact]
    public void CjkWordsAreJoinedWithoutArtificialSpaces()
    {
        var selection = new OcrTextSelection(
        [
            new OcrTextWord("截图", new OcrPixelRect(0, 0, 20, 10), 0, 0),
            new OcrTextWord("文字", new OcrPixelRect(22, 0, 20, 10), 0, 1),
            new OcrTextWord("。", new OcrPixelRect(44, 0, 5, 10), 0, 2)
        ]);

        selection.SelectAll();

        Assert.Equal("截图文字。", selection.SelectedText);
    }

    [Fact]
    public void CharacterBoxesUseGeometryForLatinWordSpacing()
    {
        var selection = new OcrTextSelection(
        [
            new OcrTextWord("P", new OcrPixelRect(0, 0, 8, 20), 0, 0),
            new OcrTextWord("I", new OcrPixelRect(9, 0, 4, 20), 0, 1),
            new OcrTextWord("D", new OcrPixelRect(14, 0, 9, 20), 0, 2),
            new OcrTextWord("5", new OcrPixelRect(31, 0, 8, 20), 0, 3),
            new OcrTextWord("8", new OcrPixelRect(40, 0, 8, 20), 0, 4)
        ]);

        selection.SelectAll();

        Assert.Equal("PID 58", selection.SelectedText);
    }
}
