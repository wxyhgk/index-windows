using Index.Capture;

namespace Index.Platform;

/// <summary>
/// 入口契约：像素怎么进来。对应 macOS 端 CaptureSource 协议。
/// 新增捕获方式 = 新增一个实现 + 注册一行，不改 CaptureCoordinator。
/// </summary>
public interface CaptureSource
{
    string Id { get; }
    string Title { get; }

    /// <summary>
    /// 冻结所有显示器画面，每块显示器一张快照。
    /// </summary>
    Task<IReadOnlyList<DisplaySnapshot>> MakeSnapshotsAsync();
}

/// <summary>
/// Read-only display-topology seam. Capture orchestration can verify the topology again after
/// freezing without knowing which concrete capture source supplied the pixels.
/// </summary>
public interface IDisplayTopologyProvider
{
    DisplayTopologySnapshot GetCurrentTopology();
}

/// <summary>
/// 单块显示器的冻结画面。
/// </summary>
public sealed class DisplaySnapshot : ICaptureDisplayIdentitySource
{
    /// <summary>Stable monitor identity for the active Windows device, independent of enumeration order.</summary>
    public required string DisplayId { get; init; }
    /// <summary>User-facing monitor name reported by Windows.</summary>
    public required string DeviceName { get; init; }
    public required int DisplayIndex { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    /// <summary>DPI 缩放倍率（物理像素 / 逻辑点）。</summary>
    public required double DpiScale { get; init; }
    public required int Left { get; init; }
    public required int Top { get; init; }
    public required byte[] PngData { get; init; }
}
