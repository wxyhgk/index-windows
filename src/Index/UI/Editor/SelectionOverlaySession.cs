using Index.Capture;
using Index.Platform;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Index.Platform.Diagnostics;
using Index.Platform.Clipboard;
using Index.Ocr;

namespace Index.UI.Editor;

/// <summary>带来源显示器的多屏选区结果。</summary>
public sealed record SelectionOverlayDecision(
    DisplaySnapshot Snapshot,
    CaptureDecision Decision);

/// <summary>
/// 一次冻结拓扑对应一个会话、每块显示器一个覆盖窗口。
/// 只协调窗口生命周期、屏间互斥和完成门，不参与捕获与裁剪。
/// </summary>
public sealed class SelectionOverlaySession : IDisposable
{
    private sealed record Entry(
        DisplaySnapshot Snapshot,
        IReadOnlyList<WindowSelectionTarget> WindowTargets,
        OverlayWindow Window);

    private readonly SelectionOverlaySessionState _state;
    private readonly List<Entry> _entries = new();
    private bool _isDismissed;
    private bool _hasShown;

    public SelectionOverlaySession(
        IReadOnlyList<DisplaySnapshot> snapshots,
        IReadOnlyList<SourceWindowInfo> windows,
        IOcrTextRecognizer ocrTextRecognizer,
        IClipboardWriter clipboardWriter,
        IAppDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count == 0)
            throw new ArgumentException("覆盖会话至少需要一张显示器快照。", nameof(snapshots));
        if (snapshots.Select(snapshot => snapshot.DisplayId).Distinct().Count() != snapshots.Count)
            throw new ArgumentException("显示器 ID 必须唯一。", nameof(snapshots));

        _state = new SelectionOverlaySessionState(snapshots.Select(snapshot => snapshot.DisplayId));
        foreach (var snapshot in snapshots)
        {
            var window = new OverlayWindow(
                ocrTextRecognizer,
                clipboardWriter,
                diagnostics);
            window.CloseOnCapture = false;
            var targets = WindowSelectionTargetMapper.Create(
                snapshot,
                windows);
            var entry = new Entry(snapshot, targets, window);
            window.InteractionActivated += OnInteractionActivated;
            window.PixelEdgeDetectionRequested += OnPixelEdgeDetectionRequested;
            window.CaptureRequested += decision => Complete(entry, decision);
            window.CancelRequested += OnCancelRequested;
            window.Closed += OnWindowClosed;
            _entries.Add(entry);
        }
    }

    public event Action<SelectionOverlayDecision>? CaptureRequested;
    public event Action<DisplaySnapshot>? PixelEdgeDetectionRequested;
    public event Action? Canceled;

    /// <summary>
    /// Controls whether the session closes its windows before publishing a capture result.
    /// The coordinator disables this for actions, such as pinning, that need the frozen frame
    /// to remain visible until the destination surface is ready.
    /// </summary>
    public bool DismissOnCapture { get; set; } = true;

    public bool IsActive => !_state.IsTerminal && !_isDismissed;

    public void SetPixelEdgeDetector(
        string displayId,
        FrozenPixelEdgeDetector? detector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayId);
        if (!IsActive)
            return;

        var entry = _entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Snapshot.DisplayId, displayId, StringComparison.OrdinalIgnoreCase));
        entry?.Window.SetPixelEdgeDetector(detector);
    }

    public void Show()
    {
        if (_isDismissed)
            throw new ObjectDisposedException(nameof(SelectionOverlaySession));
        if (_hasShown)
            throw new InvalidOperationException("覆盖会话已经显示。");
        _hasShown = true;

        try
        {
            foreach (var entry in _entries)
                entry.Window.Show(entry.Snapshot, entry.WindowTargets);

            var preferred = PreferredEntryAtCursor() ?? _entries.FirstOrDefault();
            preferred?.Window.Activate();
        }
        catch
        {
            DismissAll();
            throw;
        }
    }

    public void Cancel()
    {
        if (!_state.TryCancel()) return;

        DismissAll();
        Canceled?.Invoke();
    }

    /// <summary>再次按截图快捷键时把存活覆盖层拉回前台；原生窗口已丢失则报告失败。</summary>
    public bool TryReactivate()
    {
        if (!IsActive || _entries.Count == 0) return false;
        var preferred = PreferredEntryAtCursor();
        if (preferred is not null
            && preferred.Window.TryReactivate(preferred.Snapshot))
            return true;
        foreach (var entry in _entries)
        {
            if (!ReferenceEquals(entry, preferred)
                && entry.Window.TryReactivate(entry.Snapshot))
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        DismissAll();
        GC.SuppressFinalize(this);
    }

    private void OnInteractionActivated(OverlayWindow activeWindow)
    {
        var active = _entries.FirstOrDefault(entry => ReferenceEquals(entry.Window, activeWindow));
        if (active is null || _state.IsTerminal) return;

        var inactiveIds = _state.Activate(active.Snapshot.DisplayId).ToHashSet();
        foreach (var entry in _entries)
        {
            if (inactiveIds.Contains(entry.Snapshot.DisplayId))
                entry.Window.SetSessionInactive();
        }
    }

    private void OnCancelRequested(OverlayWindow sender)
        => Cancel();

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_isDismissed)
            Cancel();
    }

    private void Complete(Entry entry, CaptureDecision decision)
    {
        if (!_state.TryComplete(entry.Snapshot.DisplayId)) return;

        if (DismissOnCapture)
            DismissAll();
        CaptureRequested?.Invoke(new SelectionOverlayDecision(entry.Snapshot, decision));
    }

    private void OnPixelEdgeDetectionRequested(OverlayWindow window)
    {
        var entry = _entries.FirstOrDefault(candidate => ReferenceEquals(candidate.Window, window));
        if (entry is not null && IsActive)
            PixelEdgeDetectionRequested?.Invoke(entry.Snapshot);
    }

    private void DismissAll()
    {
        if (_isDismissed) return;
        _isDismissed = true;

        foreach (var entry in _entries)
        {
            entry.Window.InteractionActivated -= OnInteractionActivated;
            entry.Window.PixelEdgeDetectionRequested -= OnPixelEdgeDetectionRequested;
            entry.Window.CancelRequested -= OnCancelRequested;
            entry.Window.Closed -= OnWindowClosed;
            entry.Window.Dismiss();
        }
        _entries.Clear();
    }

    private Entry? PreferredEntryAtCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return null;
        return _entries.FirstOrDefault(entry =>
        {
            var bounds = new SourceWindowBounds(
                entry.Snapshot.Left,
                entry.Snapshot.Top,
                entry.Snapshot.Left + entry.Snapshot.Width,
                entry.Snapshot.Top + entry.Snapshot.Height);
            return cursor.X >= bounds.Left
                && cursor.X < bounds.Right
                && cursor.Y >= bounds.Top
                && cursor.Y < bounds.Bottom;
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out NativePoint point);
    }
}
