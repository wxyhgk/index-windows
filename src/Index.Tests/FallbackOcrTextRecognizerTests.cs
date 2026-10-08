using Index.Ocr;

namespace Index.Tests;

public sealed class FallbackOcrTextRecognizerTests
{
    private static readonly byte[] Png = [1, 2, 3];
    private static readonly OcrPixelRect Crop = new(0, 0, 10, 10);

    [Fact]
    public async Task PreferredRecognizerWinsWhenAvailable()
    {
        var preferred = FakeRecognizer.Success("preferred");
        var fallback = FakeRecognizer.Success("fallback");
        var recognizer = new FallbackOcrTextRecognizer(preferred, fallback);

        var result = await recognizer.RecognizeAsync(Png, Crop);

        Assert.Equal("preferred", result.LanguageTag);
        Assert.Equal(1, preferred.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task UsesFallbackWhenPreferredIsUnavailable()
    {
        var preferred = FakeRecognizer.Unavailable();
        var fallback = FakeRecognizer.Success("fallback");
        var recognizer = new FallbackOcrTextRecognizer(preferred, fallback);

        var result = await recognizer.RecognizeAsync(Png, Crop);

        Assert.Equal("fallback", result.LanguageTag);
        Assert.Equal(0, preferred.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task UsesFallbackWhenPreferredFails()
    {
        var preferred = FakeRecognizer.Failure(new InvalidDataException("model"));
        var fallback = FakeRecognizer.Success("fallback");
        var recognizer = new FallbackOcrTextRecognizer(preferred, fallback);

        var result = await recognizer.RecognizeAsync(Png, Crop);

        Assert.Equal("fallback", result.LanguageTag);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task CallerCancellationNeverFallsBack()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var preferred = FakeRecognizer.Failure(
            new OperationCanceledException(cancellation.Token));
        var fallback = FakeRecognizer.Success("fallback");
        var recognizer = new FallbackOcrTextRecognizer(preferred, fallback);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => recognizer.RecognizeAsync(Png, Crop, cancellation.Token));

        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task ReportsBothFailuresWithoutHidingEitherCause()
    {
        var recognizer = new FallbackOcrTextRecognizer(
            FakeRecognizer.Failure(new InvalidDataException("primary")),
            FakeRecognizer.Failure(new IOException("fallback")));

        var error = await Assert.ThrowsAsync<AggregateException>(
            () => recognizer.RecognizeAsync(Png, Crop));

        Assert.Collection(
            error.InnerExceptions,
            first => Assert.IsType<InvalidDataException>(first),
            second => Assert.IsType<IOException>(second));
    }

    private sealed class FakeRecognizer : IOcrTextRecognizer
    {
        private readonly Exception? _failure;
        private readonly string? _language;

        private FakeRecognizer(bool isAvailable, string? language, Exception? failure)
        {
            IsAvailable = isAvailable;
            _language = language;
            _failure = failure;
        }

        public bool IsAvailable { get; }
        public int CallCount { get; private set; }

        public static FakeRecognizer Success(string language) => new(true, language, null);
        public static FakeRecognizer Failure(Exception error) => new(true, null, error);
        public static FakeRecognizer Unavailable() => new(false, null, null);

        public Task<OcrTextResult> RecognizeAsync(
            ReadOnlyMemory<byte> frozenPng,
            OcrPixelRect crop,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _failure is not null
                ? Task.FromException<OcrTextResult>(_failure)
                : Task.FromResult(new OcrTextResult([], 10, 10, _language));
        }
    }
}
