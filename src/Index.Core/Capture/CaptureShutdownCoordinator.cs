using System.Collections.Concurrent;

namespace Index.Capture;

public readonly record struct CaptureShutdownWork
{
    public CaptureShutdownWork(string name, Task restoration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(restoration);
        Name = name;
        Restoration = restoration;
    }

    public string Name { get; }

    public Task Restoration { get; }
}

public sealed record CaptureShutdownFailure(string Name, Exception Error);

public sealed record CaptureShutdownReport(
    bool CompletedWithinBudget,
    IReadOnlyList<string> Pending,
    IReadOnlyList<CaptureShutdownFailure> Failures);

/// <summary>
/// Observes all capture restoration tasks under one total shutdown budget. The timeout only
/// limits how long the host waits; it never cancels platform lease restoration.
/// </summary>
public static class CaptureShutdownCoordinator
{
    public static async Task<CaptureShutdownReport> WaitAsync(
        IEnumerable<CaptureShutdownWork> work,
        TimeSpan totalBudget)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (totalBudget < TimeSpan.Zero && totalBudget != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(totalBudget));

        var items = work.ToArray();
        var duplicate = items
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate shutdown participant '{duplicate.Key}'.", nameof(work));

        var failures = new ConcurrentQueue<CaptureShutdownFailure>();
        var observers = items
            .Select(item => ObserveAsync(item, failures))
            .ToArray();
        var completion = Task.WhenAll(observers);
        bool completedWithinBudget;
        try
        {
            await completion.WaitAsync(totalBudget).ConfigureAwait(false);
            completedWithinBudget = true;
        }
        catch (TimeoutException)
        {
            completedWithinBudget = false;
        }

        var pending = items
            .Where(item => !item.Restoration.IsCompleted)
            .Select(item => item.Name)
            .ToArray();
        if (!completedWithinBudget && pending.Length == 0)
        {
            // Every underlying restoration crossed the finish line at the timeout boundary.
            // ObserveAsync never faults, so this await only lets its bookkeeping catch up and
            // keeps the report internally consistent.
            await completion.ConfigureAwait(false);
            completedWithinBudget = true;
        }

        return new CaptureShutdownReport(
            completedWithinBudget,
            Array.AsReadOnly(pending),
            Array.AsReadOnly(failures.ToArray()));
    }

    private static async Task ObserveAsync(
        CaptureShutdownWork work,
        ConcurrentQueue<CaptureShutdownFailure> failures)
    {
        try
        {
            await work.Restoration.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failures.Enqueue(new CaptureShutdownFailure(work.Name, error));
        }
    }
}
