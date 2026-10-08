namespace Index.Ocr;

public readonly record struct OcrPixelRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public OcrPixelRect Normalized()
    {
        double left = Math.Min(X, Right);
        double top = Math.Min(Y, Bottom);
        double right = Math.Max(X, Right);
        double bottom = Math.Max(Y, Bottom);
        return new OcrPixelRect(left, top, right - left, bottom - top);
    }

    public bool Contains(double x, double y)
    {
        var normalized = Normalized();
        return x >= normalized.X && x <= normalized.Right
            && y >= normalized.Y && y <= normalized.Bottom;
    }

    public bool Intersects(OcrPixelRect other)
    {
        var first = Normalized();
        var second = other.Normalized();
        return first.X < second.Right && first.Right > second.X
            && first.Y < second.Bottom && first.Bottom > second.Y;
    }
}

public sealed record OcrTextWord(
    string Text,
    OcrPixelRect Bounds,
    int LineIndex,
    int WordIndex);

public sealed record OcrTextResult(
    IReadOnlyList<OcrTextWord> Words,
    int PixelWidth,
    int PixelHeight,
    string? LanguageTag = null);

/// <summary>
/// Platform OCR boundary. Input is the frozen PNG plus a physical-pixel crop; results are
/// crop-relative physical-pixel values so WinUI and future model adapters share one contract.
/// </summary>
public interface IOcrTextRecognizer
{
    bool IsAvailable { get; }

    Task<OcrTextResult> RecognizeAsync(
        ReadOnlyMemory<byte> frozenPng,
        OcrPixelRect crop,
        CancellationToken cancellationToken = default);
}

