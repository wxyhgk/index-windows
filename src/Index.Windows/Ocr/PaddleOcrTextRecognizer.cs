using Index.Ocr;
using RapidOcrNet;
using SkiaSharp;

namespace Index.Platform.Ocr;

/// <summary>
/// Offline PP-OCRv4 mobile adapter. Models can be initialized by a best-effort background warmup,
/// while recognition still initializes them lazily when warmup is unavailable or incomplete.
/// </summary>
public sealed class PaddleOcrTextRecognizer : IOcrTextRecognizer, IDisposable
{
    private const string DetectorFile = "ch_PP-OCRv4_det_mobile.onnx";
    private const string ClassifierFile = "ch_ppocr_mobile_v2.0_cls_mobile.onnx";
    private const string RecognizerFile = "ch_PP-OCRv4_rec_mobile.onnx";
    private const string DictionaryFile = "ppocr_keys_v1.txt";

    private static readonly RapidOcrOptions Options = RapidOcrOptions.PythonCompat with
    {
        Padding = 24,
        DoAngle = false,
        ReturnWordBox = true,
        RecMaxDegreeOfParallelism = 2
    };

    private readonly string _modelDirectory;
    private readonly SemaphoreSlim _recognitionGate = new(1, 1);
    private RapidOcr? _engine;
    private bool _disposed;

    public PaddleOcrTextRecognizer(string? modelDirectory = null)
    {
        _modelDirectory = modelDirectory
            ?? Path.Combine(AppContext.BaseDirectory, "models", "v4");
    }

    public bool IsAvailable => !_disposed && RequiredModelPaths().All(File.Exists);

    /// <summary>
    /// Loads all ONNX sessions without requiring a screenshot. The initialized engine remains
    /// alive until this application-scoped recognizer is disposed.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAvailable)
            throw new NotSupportedException("The bundled PP-OCRv4 model files are unavailable.");

        await _recognitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _ = await EnsureEngineInitializedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _recognitionGate.Release();
        }
    }

    public async Task<OcrTextResult> RecognizeAsync(
        ReadOnlyMemory<byte> frozenPng,
        OcrPixelRect crop,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAvailable)
            throw new NotSupportedException("The bundled PP-OCRv4 model files are unavailable.");

        using var cropped = await Task.Run(
            () => DecodeCrop(frozenPng, crop, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        await _recognitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RapidOcr engine = await EnsureEngineInitializedAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var result = await engine
                .DetectAsync(cropped, Options, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return MapResult(result, cropped.Width, cropped.Height);
        }
        finally
        {
            _recognitionGate.Release();
        }
    }

    private async Task<RapidOcr> EnsureEngineInitializedAsync(
        CancellationToken cancellationToken)
    {
        if (_engine is not null)
            return _engine;

        cancellationToken.ThrowIfCancellationRequested();
        // RapidOcr model initialization itself is not cancellable. Do not pass the token to
        // Task.Run: cancellation after work starts must not abandon and leak a native engine.
        var engine = await Task.Run(CreateEngine).ConfigureAwait(false);
        if (_disposed)
        {
            engine.Dispose();
            throw new ObjectDisposedException(nameof(PaddleOcrTextRecognizer));
        }

        _engine = engine;
        cancellationToken.ThrowIfCancellationRequested();
        return engine;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _recognitionGate.Wait();
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            _engine?.Dispose();
            _engine = null;
        }
        finally
        {
            _recognitionGate.Release();
        }
    }

    private RapidOcr CreateEngine()
    {
        var engine = new RapidOcr();
        try
        {
            engine.InitModels(
                Path.Combine(_modelDirectory, DetectorFile),
                Path.Combine(_modelDirectory, ClassifierFile),
                Path.Combine(_modelDirectory, RecognizerFile),
                Path.Combine(_modelDirectory, DictionaryFile));
            return engine;
        }
        catch
        {
            engine.Dispose();
            throw;
        }
    }

    private IEnumerable<string> RequiredModelPaths()
    {
        yield return Path.Combine(_modelDirectory, DetectorFile);
        yield return Path.Combine(_modelDirectory, ClassifierFile);
        yield return Path.Combine(_modelDirectory, RecognizerFile);
        yield return Path.Combine(_modelDirectory, DictionaryFile);
    }

    private static SKBitmap DecodeCrop(
        ReadOnlyMemory<byte> frozenPng,
        OcrPixelRect crop,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var source = SKBitmap.Decode(frozenPng.ToArray())
            ?? throw new InvalidDataException("The frozen screenshot could not be decoded.");
        var normalized = crop.Normalized();
        int left = Math.Clamp((int)Math.Floor(normalized.X), 0, source.Width - 1);
        int top = Math.Clamp((int)Math.Floor(normalized.Y), 0, source.Height - 1);
        int right = Math.Clamp((int)Math.Ceiling(normalized.Right), left + 1, source.Width);
        int bottom = Math.Clamp((int)Math.Ceiling(normalized.Bottom), top + 1, source.Height);
        int width = right - left;
        int height = bottom - top;

        var output = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            using var canvas = new SKCanvas(output);
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(
                source,
                new SKRect(left, top, right, bottom),
                new SKRect(0, 0, width, height));
            cancellationToken.ThrowIfCancellationRequested();
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private static OcrTextResult MapResult(OcrResult result, int width, int height)
    {
        var words = new List<OcrTextWord>();
        for (int lineIndex = 0; lineIndex < result.TextBlocks.Length; lineIndex++)
        {
            var block = result.TextBlocks[lineIndex];
            var blockWords = block.WordResults;
            if (blockWords is { Length: > 0 })
            {
                for (int wordIndex = 0; wordIndex < blockWords.Length; wordIndex++)
                {
                    var word = blockWords[wordIndex];
                    if (!string.IsNullOrWhiteSpace(word.Text))
                    {
                        words.Add(new OcrTextWord(
                            word.Text,
                            BoundsOf(word.BoxPoints),
                            lineIndex,
                            wordIndex));
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(block.Text))
            {
                words.Add(new OcrTextWord(
                    block.Text,
                    BoundsOf(block.BoxPoints),
                    lineIndex,
                    0));
            }
        }

        return new OcrTextResult(
            words.AsReadOnly(),
            width,
            height,
            "ppocr-v4-zh-light");
    }

    private static OcrPixelRect BoundsOf(IReadOnlyList<SKPointI> points)
    {
        if (points.Count == 0)
            return default;

        int left = points.Min(point => point.X);
        int top = points.Min(point => point.Y);
        int right = points.Max(point => point.X);
        int bottom = points.Max(point => point.Y);
        return new OcrPixelRect(left, top, right - left, bottom - top);
    }
}
