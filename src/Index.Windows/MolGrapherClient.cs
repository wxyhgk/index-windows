using System.Net.Http.Headers;
using System.Text.Json;
using Index.Molecule;
using Index.Recognition;

namespace Index.Platform;

/// <summary>HTTP adapter for the local MolGrapher recognition service.</summary>
public sealed class MolGrapherClient : IRecognitionPlugin<MoleculeRecognitionResult>, IDisposable
{
    private static readonly HttpClient Http = new()
    {
        // The first request may generate and compile the OpenVINO IR cache.
        Timeout = TimeSpan.FromMinutes(5)
    };

    private readonly string _baseUrl;

    public RecognitionPluginDescriptor Descriptor { get; } = new(
        Id: "molgrapher.local",
        DisplayName: "MolGrapher",
        Capability: RecognitionCapabilities.MoleculeStructure,
        Version: new Version(1, 0),
        Priority: 100);

    public MolGrapherClient(string baseUrl = "http://127.0.0.1:8100")
    {
        _baseUrl = baseUrl;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync($"{_baseUrl}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<MoleculeRecognitionResult> RecognizeAsync(
        RecognitionInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.Kind != RecognitionInputKind.Image)
        {
            throw new NotSupportedException(
                $"MolGrapher does not support recognition input kind '{input.Kind}'.");
        }

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(input.Content.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            input.MediaType ?? "application/octet-stream");
        content.Add(fileContent, "file", "capture.png");

        var response = await Http.PostAsync(
            $"{_baseUrl}/recognize",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        return new MoleculeRecognitionResult(
            Smiles: root.TryGetProperty("smi", out var smi)
                && smi.ValueKind == JsonValueKind.String ? smi.GetString() : null,
            Confidence: root.TryGetProperty("confidence", out var confidence)
                ? confidence.GetDouble() : 0,
            Sdf: root.TryGetProperty("sdf", out var sdf)
                && sdf.ValueKind == JsonValueKind.String ? sdf.GetString() : null,
            ProcessingTimeMs: root.TryGetProperty("processing_time_ms", out var elapsed)
                ? elapsed.GetInt32() : 0,
            Error: root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String ? error.GetString() : null);
    }

    public void Dispose()
    {
        // HttpClient is process-wide and intentionally reused.
    }
}
