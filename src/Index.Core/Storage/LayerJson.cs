using System.Text.Json;
using System.Text.Json.Serialization;
using Index.Annotation;

namespace Index.Storage;

internal static class LayerJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(Layers<ImageSpace> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        return JsonSerializer.Serialize(
            layers.Elements.Select(layer => new PersistedLayer(
                layer.Id,
                layer.Kind,
                new PersistedRect(layer.Rect.X, layer.Rect.Y, layer.Rect.W, layer.Rect.H),
                new PersistedColor(layer.Color.R, layer.Color.G, layer.Color.B, layer.Color.A),
                layer.LineWidth,
                layer.Text,
                layer.FontSize,
                layer.BlockScale,
                layer.Dim)),
            Options);
    }

    public static Layers<ImageSpace> Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var persisted = JsonSerializer.Deserialize<PersistedLayer[]>(json, Options)
            ?? throw new JsonException("The annotation revision is empty.");
        var layers = new Layers<ImageSpace>();
        foreach (var item in persisted)
        {
            layers.Append(new Layer(
                item.Kind,
                new LRect(item.Rect.X, item.Rect.Y, item.Rect.W, item.Rect.H),
                new LColor(item.Color.R, item.Color.G, item.Color.B, item.Color.A),
                item.LineWidth,
                item.Text,
                item.FontSize)
            {
                Id = item.Id,
                BlockScale = item.BlockScale,
                Dim = item.Dim
            });
        }
        return layers;
    }

    // 不直接序列化领域对象：Layer/LRect 上有 HandleBounds、MinX 等计算属性，
    // 它们不是跨平台 layers-v1 契约的一部分。
    private sealed record PersistedLayer(
        Guid Id,
        LayerKind Kind,
        PersistedRect Rect,
        PersistedColor Color,
        double LineWidth,
        string Text,
        double FontSize,
        double? BlockScale,
        double? Dim);

    private sealed record PersistedRect(double X, double Y, double W, double H);
    private sealed record PersistedColor(double R, double G, double B, double A);
}
