namespace Index.Actions;

/// <summary>
/// 结束截图会话。原图、来源元数据和修订链已经由 CaptureCoordinator 原子入库，
/// 此动作刻意不再向桌面导出第二份文件。
/// </summary>
public sealed class CompleteCaptureAction : ICaptureAction
{
    private static readonly IReadOnlySet<CaptureActionScope> SupportedScopes =
        new HashSet<CaptureActionScope> { CaptureActionScope.Capture };

    public CaptureActionDescriptor Descriptor { get; } = new(
        CaptureActionIds.Complete,
        "完成",
        "✓",
        SupportedScopes);

    public ValueTask PerformAsync(
        CaptureContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
