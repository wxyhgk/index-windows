namespace Index.Ocr;

/// <summary>
/// Prefers a bundled recognizer while retaining an installed platform recognizer as a
/// non-fatal fallback. Cancellation is always owned by the caller and is never swallowed.
/// </summary>
public sealed class FallbackOcrTextRecognizer : IOcrTextRecognizer
{
    private readonly IOcrTextRecognizer _primary;
    private readonly IOcrTextRecognizer _fallback;

    public FallbackOcrTextRecognizer(
        IOcrTextRecognizer primary,
        IOcrTextRecognizer fallback)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public bool IsAvailable => _primary.IsAvailable || _fallback.IsAvailable;

    public async Task<OcrTextResult> RecognizeAsync(
        ReadOnlyMemory<byte> frozenPng,
        OcrPixelRect crop,
        CancellationToken cancellationToken = default)
    {
        Exception? primaryFailure = null;
        if (_primary.IsAvailable)
        {
            try
            {
                return await _primary
                    .RecognizeAsync(frozenPng, crop, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                primaryFailure = error;
            }
        }

        if (_fallback.IsAvailable)
        {
            try
            {
                return await _fallback
                    .RecognizeAsync(frozenPng, crop, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception fallbackFailure) when (primaryFailure is not null)
            {
                throw new AggregateException(
                    "Both OCR recognizers failed.",
                    primaryFailure,
                    fallbackFailure);
            }
        }

        if (primaryFailure is not null)
            throw new InvalidOperationException(
                "The preferred OCR recognizer failed and no fallback is available.",
                primaryFailure);

        throw new NotSupportedException("No OCR recognizer is available.");
    }
}
