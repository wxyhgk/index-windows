namespace Index.Annotation;

/// <summary>
/// 图层坐标空间标记。C# 没有 phantom type，用泛型参数标记。
/// 对应 macOS 端 LayerSpace / CanvasSpace / ImageSpace。
/// </summary>
public abstract class LayerSpace { }

/// <summary>画布空间：编辑时的工作坐标系，左上原点、Y 向下。</summary>
public sealed class CanvasSpace : LayerSpace { }

/// <summary>图像像素空间：入库和导出只接受这个空间。</summary>
public sealed class ImageSpace : LayerSpace { }

/// <summary>
/// 带空间标记的图层集合。
/// 对应 macOS 端 Layers&lt;Space&gt;。
/// </summary>
public sealed class Layers<TSpace> where TSpace : LayerSpace
{
    public List<Layer> Elements { get; } = new();

    public bool IsEmpty => Elements.Count == 0;
    public int Count => Elements.Count;

    public void Append(Layer layer) => Elements.Add(layer);

    public void Insert(Layer layer, int index)
    {
        int clamped = Math.Clamp(index, 0, Elements.Count);
        Elements.Insert(clamped, layer);
    }

    public Layer? RemoveLast()
    {
        if (Elements.Count == 0) return null;
        var last = Elements[^1];
        Elements.RemoveAt(Elements.Count - 1);
        return last;
    }

    public void RemoveAll() => Elements.Clear();

    public void Remove(Guid id) => Elements.RemoveAll(l => l.Id == id);

    public int? FirstIndex(Guid id)
    {
        for (int i = 0; i < Elements.Count; i++)
            if (Elements[i].Id == id) return i;
        return null;
    }

    public Layer this[int index]
    {
        get => Elements[index];
        set => Elements[index] = value;
    }

    /// <summary>
    /// 画布空间 → 图像像素空间。唯一的桥。
    /// </summary>
    public Layers<ImageSpace> Projected(LRect selection, double scale)
        => Projected(selection, scale, scale);

    /// <summary>
    /// Canvas space to image-pixel space, preserving independent display axes.
    /// Stroke and font sizes use the geometric mean so their visual weight stays stable.
    /// </summary>
    public Layers<ImageSpace> Projected(LRect selection, double scaleX, double scaleY)
    {
        double visualScale = Math.Sqrt(Math.Abs(scaleX * scaleY));
        var result = new Layers<ImageSpace>();
        foreach (var layer in Elements)
        {
            var copy = layer with
            {
                Rect = new LRect(
                    (layer.Rect.X - selection.X) * scaleX,
                    (layer.Rect.Y - selection.Y) * scaleY,
                    layer.Rect.W * scaleX,
                    layer.Rect.H * scaleY),
                LineWidth = layer.LineWidth * visualScale,
                FontSize = layer.FontSize * visualScale
            };
            result.Append(copy);
        }
        return result;
    }
}
