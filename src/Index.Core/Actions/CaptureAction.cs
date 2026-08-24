using Index.Capture;

namespace Index.Actions;

public enum CaptureActionScope
{
    Capture,
    Pinned
}

/// <summary>工具栏展示动作所需的稳定元数据。</summary>
public sealed record CaptureActionDescriptor(
    string Id,
    string Title,
    string Glyph,
    IReadOnlySet<CaptureActionScope> Scopes,
    bool IsPrimary = true);

/// <summary>动作可以要求宿主执行的最小生命周期操作。</summary>
public interface ICaptureActionHost
{
    void Dismiss();
}

/// <summary>一次动作执行拿到的全部材料。</summary>
public sealed class CaptureContext
{
    public required CaptureArtifact Artifact { get; init; }
    public CaptureRegion? Region { get; init; }
    public string? SuggestedFileName { get; init; }
    public ICaptureActionHost? Host { get; init; }
}

/// <summary>用户可以对截图产物执行的动作。</summary>
public interface ICaptureAction
{
    CaptureActionDescriptor Descriptor { get; }
    bool SuppressesAutoCopy => false;

    ValueTask PerformAsync(
        CaptureContext context,
        CancellationToken cancellationToken = default);
}

public static class CaptureActionIds
{
    public const string Complete = "complete";
    public const string Save = "save";
    public const string Copy = "copy";
    public const string Pin = "pin";
    public const string Close = "close";
}
