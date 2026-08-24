using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Index.Platform;

/// <summary>MolGrapher 本地推理服务客户端（localhost:8100）。</summary>
public sealed class MolGrapherClient : IDisposable
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly string _baseUrl;

    public MolGrapherClient(string baseUrl = "http://127.0.0.1:8100")
    {
        _baseUrl = baseUrl;
    }

    public sealed record RecognizeResult(
        string? Smiles,
        double Confidence,
        string? Sdf,
        int ProcessingTimeMs,
        string? Error);

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await Http.GetAsync($"{_baseUrl}/health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<RecognizeResult> RecognizeAsync(byte[] pngData, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pngData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "capture.png");

        var resp = await Http.PostAsync($"{_baseUrl}/recognize", content, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        return new RecognizeResult(
            Smiles: root.TryGetProperty("smi", out var smi) && smi.ValueKind == JsonValueKind.String ? smi.GetString() : null,
            Confidence: root.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0,
            Sdf: root.TryGetProperty("sdf", out var sdf) && sdf.ValueKind == JsonValueKind.String ? sdf.GetString() : null,
            ProcessingTimeMs: root.TryGetProperty("processing_time_ms", out var ms) ? ms.GetInt32() : 0,
            Error: root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String ? err.GetString() : null);
    }

    public void Dispose()
    {
        // HttpClient 是 static，不需要释放
    }
}
