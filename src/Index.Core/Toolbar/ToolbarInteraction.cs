namespace Index.Toolbar;

/// <summary>在当前可用控件 ID 中循环键盘焦点。</summary>
public static class ToolbarFocusNavigator
{
    public static string? Next(IReadOnlyList<string> ids, string? current, int offset)
    {
        if (ids.Count == 0 || offset == 0) return current;
        int currentIndex = current is null ? -1 : IndexOf(ids, current);
        if (currentIndex < 0) return offset > 0 ? ids[0] : ids[^1];
        int next = ((currentIndex + offset) % ids.Count + ids.Count) % ids.Count;
        return ids[next];
    }

    private static int IndexOf(IReadOnlyList<string> ids, string target)
    {
        for (int index = 0; index < ids.Count; index++)
            if (ids[index] == target) return index;
        return -1;
    }
}

/// <summary>把触控板连续滚动量累积成离散的样式档位步进。</summary>
public sealed class ToolbarScrollAccumulator
{
    public const double PreciseThreshold = 6;
    public double Value { get; private set; }

    public void Reset() => Value = 0;

    public int? Step(double delta, bool isPrecise)
    {
        if (delta == 0) return null;
        if (!isPrecise)
        {
            Value = 0;
            return delta > 0 ? 1 : -1;
        }

        Value += delta;
        if (Math.Abs(Value) < PreciseThreshold) return null;
        int step = Value > 0 ? 1 : -1;
        Value -= step * PreciseThreshold;
        return step;
    }
}
