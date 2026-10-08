using System.Runtime.InteropServices.WindowsRuntime;
using Index.Ocr;
using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Index.Platform.Ocr;

/// <summary>Offline OCR backed by the language packs already installed in Windows.</summary>
public sealed class WindowsOcrTextRecognizer : IOcrTextRecognizer
{
    private readonly bool _isAvailable;

    public WindowsOcrTextRecognizer()
    {
        try
        {
            _isAvailable = OcrEngine.TryCreateFromUserProfileLanguages() is not null;
        }
        catch
        {
            _isAvailable = false;
        }
    }

    public bool IsAvailable => _isAvailable;

    public async Task<OcrTextResult> RecognizeAsync(
        ReadOnlyMemory<byte> frozenPng,
        OcrPixelRect crop,
        CancellationToken cancellationToken = default)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new NotSupportedException(
                "Windows OCR is unavailable for the installed user languages.");
        cancellationToken.ThrowIfCancellationRequested();

        // PowerToys Text Extractor and Text Grab both pass an encoded image stream through
        // BitmapDecoder before invoking Windows OCR. Do the same here instead of manually
        // constructing a SoftwareBitmap and copying a raw pixel buffer into it.
        var prepared = await Task.Run(
            () => PrepareEncodedCrop(frozenPng, crop, cancellationToken),
            cancellationToken);
        using var encodedStream = new MemoryStream(prepared.EncodedPng, writable: false);
        using var randomAccessStream = encodedStream.AsRandomAccessStream();
        var decoder = await BitmapDecoder
            .CreateAsync(randomAccessStream)
            .AsTask(cancellationToken);
        using var bitmap = await decoder
            .GetSoftwareBitmapAsync()
            .AsTask(cancellationToken);
        var recognized = await engine
            .RecognizeAsync(bitmap)
            .AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var words = new List<OcrTextWord>();
        for (int lineIndex = 0; lineIndex < recognized.Lines.Count; lineIndex++)
        {
            var line = recognized.Lines[lineIndex];
            for (int wordIndex = 0; wordIndex < line.Words.Count; wordIndex++)
            {
                var word = line.Words[wordIndex];
                var bounds = word.BoundingRect;
                words.Add(new OcrTextWord(
                    word.Text,
                    new OcrPixelRect(
                        bounds.X / prepared.Scale,
                        bounds.Y / prepared.Scale,
                        bounds.Width / prepared.Scale,
                        bounds.Height / prepared.Scale),
                    lineIndex,
                    wordIndex));
            }
        }

        return new OcrTextResult(
            words.AsReadOnly(),
            prepared.SourceWidth,
            prepared.SourceHeight,
            engine.RecognizerLanguage?.LanguageTag);
    }

    private static PreparedOcrImage PrepareEncodedCrop(
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
        int sourceWidth = right - left;
        int sourceHeight = bottom - top;
        // PowerToys enlarges ordinary captures before OCR. UI screenshots often contain
        // 12-18 px glyphs, so use 2x when the legacy engine's dimension limit allows it.
        // This is still only resampling (not invented detail), but it preserves anti-aliased
        // stroke shapes better for the recognizer's fixed-size input stages.
        double scale = Math.Min(
            2.0,
            OcrEngine.MaxImageDimension / (double)Math.Max(sourceWidth, sourceHeight));
        int outputWidth = Math.Max(1, (int)Math.Floor(sourceWidth * scale));
        int outputHeight = Math.Max(1, (int)Math.Floor(sourceHeight * scale));

        using var rendered = new SKBitmap(
            outputWidth,
            outputHeight,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using (var canvas = new SKCanvas(rendered))
        {
            // OCR has no useful alpha semantics. Flattening onto white also avoids transparent
            // edge pixels being interpreted differently by bitmap decoder implementations.
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(
                source,
                new SKRect(left, top, right, bottom),
                new SKRect(0, 0, outputWidth, outputHeight),
                paint);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var image = SKImage.FromBitmap(rendered);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidDataException("The OCR crop could not be encoded.");
        return new PreparedOcrImage(
            encoded.ToArray(),
            scale,
            sourceWidth,
            sourceHeight);
    }

    private sealed record PreparedOcrImage(
        byte[] EncodedPng,
        double Scale,
        int SourceWidth,
        int SourceHeight);
}
