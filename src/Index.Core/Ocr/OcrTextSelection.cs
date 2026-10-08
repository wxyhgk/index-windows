namespace Index.Ocr;

/// <summary>Pure selection and reading-order rules for the interactive OCR overlay.</summary>
public sealed class OcrTextSelection
{
    private const double ClickTolerance = 3;
    private readonly IReadOnlyList<OcrTextWord> _words;
    private readonly HashSet<int> _selected = [];

    public OcrTextSelection(IReadOnlyList<OcrTextWord> words)
    {
        _words = words ?? throw new ArgumentNullException(nameof(words));
    }

    public IReadOnlySet<int> SelectedIndices => _selected;

    public string SelectedText => BuildText(_selected);

    public int HitTest(double x, double y, double tolerance = 0)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            return -1;

        double safeTolerance = Math.Max(0, tolerance);
        return Enumerable.Range(0, _words.Count)
            .Where(index => Contains(_words[index].Bounds, x, y, safeTolerance))
            .OrderBy(index => _words[index].Bounds.Width * _words[index].Bounds.Height)
            .FirstOrDefault(-1);
    }

    public void Select(OcrPixelRect region)
    {
        _selected.Clear();
        var normalized = region.Normalized();
        if (normalized.Width <= ClickTolerance && normalized.Height <= ClickTolerance)
        {
            double x = normalized.X + normalized.Width / 2;
            double y = normalized.Y + normalized.Height / 2;
            int match = HitTest(x, y);
            if (match >= 0)
                _selected.Add(match);
            return;
        }

        for (int index = 0; index < _words.Count; index++)
        {
            if (_words[index].Bounds.Intersects(normalized))
                _selected.Add(index);
        }
    }

    /// <summary>
    /// Selects an inclusive range in OCR reading order. Unlike rectangle selection, a narrow
    /// vertical drag includes the remainder of the first line, every intermediate line, and the
    /// beginning of the final line, matching native text-selection behavior.
    /// </summary>
    public void SelectReadingRange(
        double anchorX,
        double anchorY,
        double focusX,
        double focusY,
        double tolerance = 0)
    {
        _selected.Clear();
        if (_words.Count == 0
            || !double.IsFinite(anchorX)
            || !double.IsFinite(anchorY)
            || !double.IsFinite(focusX)
            || !double.IsFinite(focusY))
        {
            return;
        }

        int[] readingOrder = Enumerable.Range(0, _words.Count)
            .OrderBy(index => _words[index].LineIndex)
            .ThenBy(index => _words[index].WordIndex)
            .ThenBy(index => index)
            .ToArray();
        int anchor = ResolveRangeEndpoint(anchorX, anchorY, tolerance);
        int focus = ResolveRangeEndpoint(focusX, focusY, tolerance);
        int anchorPosition = Array.IndexOf(readingOrder, anchor);
        int focusPosition = Array.IndexOf(readingOrder, focus);
        if (anchorPosition < 0 || focusPosition < 0)
            return;

        int first = Math.Min(anchorPosition, focusPosition);
        int last = Math.Max(anchorPosition, focusPosition);
        for (int position = first; position <= last; position++)
            _selected.Add(readingOrder[position]);
    }

    public void SelectAll()
    {
        _selected.Clear();
        for (int index = 0; index < _words.Count; index++)
            _selected.Add(index);
    }

    public void Clear() => _selected.Clear();

    private int ResolveRangeEndpoint(double x, double y, double tolerance)
    {
        int exact = HitTest(x, y, tolerance);
        if (exact >= 0)
            return exact;

        // Pointer coordinates commonly land in the whitespace between OCR boxes. Resolve the
        // closest visual line first, then the closest word on that line; doing this geometrically
        // avoids depending on recognizer-specific line-number spacing.
        var closestLine = Enumerable.Range(0, _words.Count)
            .GroupBy(index => _words[index].LineIndex)
            .OrderBy(line => line.Min(index => AxisDistance(_words[index].Bounds, y, vertical: true)))
            .ThenBy(line => line.Key)
            .First();
        return closestLine
            .OrderBy(index => AxisDistance(_words[index].Bounds, x, vertical: false))
            .ThenBy(index => _words[index].WordIndex)
            .ThenBy(index => index)
            .First();
    }

    private static double AxisDistance(OcrPixelRect bounds, double value, bool vertical)
    {
        var normalized = bounds.Normalized();
        double start = vertical ? normalized.Y : normalized.X;
        double end = vertical ? normalized.Bottom : normalized.Right;
        if (value < start)
            return start - value;
        if (value > end)
            return value - end;
        return 0;
    }

    private static bool Contains(
        OcrPixelRect bounds,
        double x,
        double y,
        double tolerance)
    {
        var normalized = bounds.Normalized();
        return x >= normalized.X - tolerance
            && x <= normalized.Right + tolerance
            && y >= normalized.Y - tolerance
            && y <= normalized.Bottom + tolerance;
    }

    private string BuildText(IEnumerable<int> indices)
    {
        return string.Join(
            Environment.NewLine,
            indices
                .Select(index => _words[index])
                .OrderBy(word => word.LineIndex)
                .ThenBy(word => word.WordIndex)
                .GroupBy(word => word.LineIndex)
                .Select(JoinLine)
                .Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string JoinLine(IEnumerable<OcrTextWord> words)
    {
        var ordered = words.ToArray();
        if (ordered.Length == 0)
            return "";

        var text = new System.Text.StringBuilder(ordered[0].Text);
        for (int index = 1; index < ordered.Length; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];
            if (NeedsSpace(previous, current))
                text.Append(' ');
            text.Append(current.Text);
        }
        return text.ToString();
    }

    private static bool NeedsSpace(OcrTextWord previousWord, OcrTextWord currentWord)
    {
        string previous = previousWord.Text;
        string current = currentWord.Text;
        if (string.IsNullOrEmpty(previous) || string.IsNullOrEmpty(current))
            return false;
        char left = previous[^1];
        char right = current[0];
        if (IsCjk(left) || IsCjk(right))
            return false;
        if (char.IsPunctuation(right) && right is not '(' and not '[' and not '{')
            return false;

        // PP-OCR emits per-character boxes for a line containing any CJK glyph. Do not
        // manufacture spaces between the Latin characters in those mixed lines; only a
        // visibly larger geometric gap represents an actual word boundary.
        if (previous.Length == 1
            && current.Length == 1
            && char.IsLetterOrDigit(left)
            && char.IsLetterOrDigit(right))
        {
            var previousBounds = previousWord.Bounds.Normalized();
            var currentBounds = currentWord.Bounds.Normalized();
            double gap = currentBounds.X - previousBounds.Right;
            double glyphHeight = Math.Min(previousBounds.Height, currentBounds.Height);
            return gap > Math.Max(2, glyphHeight * 0.28);
        }

        return left is not '(' and not '[' and not '{';
    }

    private static bool IsCjk(char value) =>
        value is >= '\u3400' and <= '\u9FFF'
        or >= '\uF900' and <= '\uFAFF'
        or >= '\u3040' and <= '\u30FF'
        or >= '\uAC00' and <= '\uD7AF';
}
