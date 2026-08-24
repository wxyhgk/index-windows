using Index.Annotation;
using Index.Render;

namespace Index.Capture;

/// <summary>截图在屏幕上的物理像素区域。</summary>
public readonly record struct CaptureRegion(int X, int Y, int Width, int Height);

/// <summary>
/// 一次截图的不可变像素产物。底图和图层在构造时冻结，最终 PNG 惰性合成且只合成一次。
/// </summary>
public sealed class CaptureArtifact
{
    private readonly byte[] _basePng;
    private readonly Layers<ImageSpace> _layers;
    private readonly object _renderLock = new();
    private Task<byte[]>? _renderTask;

    public CaptureArtifact(byte[] basePng, Layers<ImageSpace> layers)
    {
        ArgumentNullException.ThrowIfNull(basePng);
        ArgumentNullException.ThrowIfNull(layers);

        _basePng = (byte[])basePng.Clone();
        _layers = CloneLayers(layers);
    }

    /// <summary>取得带标注的 PNG。多个动作共享同一项后台合成任务。</summary>
    public async ValueTask<ReadOnlyMemory<byte>> GetRenderedPngAsync(
        CancellationToken cancellationToken = default)
    {
        Task<byte[]> renderTask;
        lock (_renderLock)
        {
            _renderTask ??= Task.Run(
                () => CaptureArtifactRenderer.RenderPng(_basePng, _layers),
                CancellationToken.None);
            renderTask = _renderTask;
        }

        var png = await renderTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        return png;
    }

    private static Layers<ImageSpace> CloneLayers(Layers<ImageSpace> source)
    {
        var result = new Layers<ImageSpace>();
        foreach (var layer in source.Elements)
            result.Append(layer with { });
        return result;
    }
}
