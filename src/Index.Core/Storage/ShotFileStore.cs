using System.Security.Cryptography;
using SkiaSharp;

namespace Index.Storage;

internal sealed class ShotFileStore
{
    public string RootDirectory { get; }
    public string OriginalsDirectory { get; }
    public string ThumbnailsDirectory { get; }

    public ShotFileStore(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        OriginalsDirectory = Path.Combine(RootDirectory, "originals");
        ThumbnailsDirectory = Path.Combine(RootDirectory, "thumbnails");
        Directory.CreateDirectory(OriginalsDirectory);
        Directory.CreateDirectory(ThumbnailsDirectory);
    }

    public static string ComputeSha256(ReadOnlySpan<byte> bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public string OriginalPath(string sha256) => Path.Combine(OriginalsDirectory, $"{sha256}.png");
    public string ThumbnailPath(string sha256) => Path.Combine(ThumbnailsDirectory, $"{sha256}.jpg");

    public async Task<bool> WriteOriginalAsync(
        string sha256,
        ReadOnlyMemory<byte> png,
        CancellationToken cancellationToken)
    {
        var target = OriginalPath(sha256);
        if (File.Exists(target))
            return false;

        var stage = Path.Combine(OriginalsDirectory, $".{sha256}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(stage, png.ToArray(), cancellationToken);
            try
            {
                File.Move(stage, target, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(target))
            {
                return false;
            }
        }
        finally
        {
            if (File.Exists(stage))
                File.Delete(stage);
        }
    }

    public void EnsureThumbnail(string sha256, ReadOnlySpan<byte> png)
    {
        var target = ThumbnailPath(sha256);
        if (File.Exists(target))
            return;

        using var source = SKBitmap.Decode(png.ToArray());
        if (source is null)
            return;

        const int maxDimension = 640;
        var ratio = Math.Min(1d, maxDimension / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * ratio));
        var height = Math.Max(1, (int)Math.Round(source.Height * ratio));
        using var resized = source.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default);
        if (resized is null)
            return;
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 84);
        if (encoded is null)
            return;
        var stage = Path.Combine(ThumbnailsDirectory, $".{sha256}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = File.Create(stage))
                encoded.SaveTo(stream);
            try
            {
                File.Move(stage, target, overwrite: false);
            }
            catch (IOException) when (File.Exists(target))
            {
            }
        }
        finally
        {
            if (File.Exists(stage))
                File.Delete(stage);
        }
    }

    public void Delete(string sha256)
    {
        File.Delete(OriginalPath(sha256));
        File.Delete(ThumbnailPath(sha256));
    }
}
