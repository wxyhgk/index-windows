using Index.UI.Gallery;
using SkiaSharp;

namespace Index.Tests;

public sealed class ThumbnailLoaderTests
{
    [Fact]
    public async Task CacheSharesDecodeAndLeaseSurvivesInvalidation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"index-thumbnail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sample.png");
        try
        {
            using (var bitmap = new SKBitmap(32, 24))
            using (var canvas = new SKCanvas(bitmap))
            using (var image = SKImage.FromBitmap(bitmap))
            using (var encoded = image.Encode(SKEncodedImageFormat.Png, 100))
            {
                canvas.Clear(SKColors.CornflowerBlue);
                await using var stream = File.Create(path);
                encoded.SaveTo(stream);
            }

            using var loader = new ThumbnailLoader(new ThumbnailLoaderOptions
            {
                MaxConcurrentLoads = 2,
                MaxCacheBytes = 1024 * 1024,
                MaxCacheItems = 2,
                MaxPixelDimension = 64
            });
            var firstTask = loader.LoadAsync(path).AsTask();
            var secondTask = loader.LoadAsync(path).AsTask();
            using var first = await firstTask;
            using var second = await secondTask;

            Assert.Equal(1, loader.CachedItemCount);
            Assert.Same(first.Bitmap, second.Bitmap);
            loader.Invalidate(path);
            Assert.Equal(32, first.Bitmap.Width);
            Assert.Equal(24, second.Bitmap.Height);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
