using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Index.Storage;

namespace Index.Platform;

[SupportedOSPlatform("windows6.1")]
public sealed class WindowsShotAssetReader : IShotAssetReader
{
    private readonly ShotStore _store;

    public WindowsShotAssetReader(ShotStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ShotAssetReadResult> ReadBestAvailableAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shot);

        var original = await ReadCandidateAsync(
            _store.OriginalPath(shot),
            ShotAssetStatus.Original,
            cancellationToken);
        if (original.HasData)
            return original;

        var thumbnail = await ReadFirstThumbnailAsync(shot, cancellationToken);
        if (thumbnail.HasData)
        {
            var reason = original.Status == ShotAssetStatus.Corrupt
                ? "原图损坏，当前使用缩略图"
                : "原图缺失，当前使用缩略图";
            return thumbnail with
            {
                Status = ShotAssetStatus.ThumbnailFallback,
                Warning = reason
            };
        }

        return FailureResult(original, thumbnail);
    }

    public async Task<ShotAssetReadResult> ReadPreviewAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shot);

        // Lossless thumbnail migration is a storage concern. Keeping it in this
        // adapter prevents views from learning the concrete on-disk layout.
        try
        {
            await _store.EnsureLosslessThumbnailAsync(shot, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Lossless thumbnail migration failed for shot {shot.Id}: {error.Message}");
        }

        var thumbnail = await ReadFirstThumbnailAsync(shot, cancellationToken);
        if (thumbnail.HasData)
            return thumbnail with { Status = ShotAssetStatus.ThumbnailFallback };

        var original = await ReadCandidateAsync(
            _store.OriginalPath(shot),
            ShotAssetStatus.Original,
            cancellationToken);
        if (original.HasData)
        {
            return original with
            {
                Warning = thumbnail.Status == ShotAssetStatus.Corrupt
                    ? "缩略图损坏，当前使用原图预览"
                    : null
            };
        }

        return FailureResult(original, thumbnail);
    }

    private async Task<ShotAssetReadResult> ReadFirstThumbnailAsync(
        ShotRecord shot,
        CancellationToken cancellationToken)
    {
        var lossless = await ReadCandidateAsync(
            _store.ThumbnailPath(shot),
            ShotAssetStatus.ThumbnailFallback,
            cancellationToken);
        if (lossless.HasData)
            return lossless;

        var legacy = await ReadCandidateAsync(
            _store.LegacyThumbnailPath(shot),
            ShotAssetStatus.ThumbnailFallback,
            cancellationToken);
        if (legacy.HasData)
            return legacy;

        return lossless.Status == ShotAssetStatus.Corrupt
            ? lossless
            : legacy;
    }

    private static async Task<ShotAssetReadResult> ReadCandidateAsync(
        string path,
        ShotAssetStatus successStatus,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return Missing();

        try
        {
            var data = await File.ReadAllBytesAsync(path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryNormalizeToPng(data, out var png))
                return Corrupt($"图片文件损坏：{Path.GetFileName(path)}");
            return new ShotAssetReadResult(successStatus, png);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Corrupt($"图片文件无法读取：{Path.GetFileName(path)}");
        }
    }

    private static bool TryNormalizeToPng(byte[] data, out byte[] png)
    {
        png = [];
        if (data.Length == 0)
            return false;
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var image = Image.FromStream(
                stream,
                useEmbeddedColorManagement: false,
                validateImageData: true);
            if (image.Width <= 0 || image.Height <= 0)
                return false;
            if (image.RawFormat.Guid == ImageFormat.Png.Guid)
            {
                png = data;
                return true;
            }

            using var output = new MemoryStream();
            image.Save(output, ImageFormat.Png);
            png = output.ToArray();
            return png.Length > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (OutOfMemoryException)
        {
            return false;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private static ShotAssetReadResult FailureResult(
        ShotAssetReadResult original,
        ShotAssetReadResult thumbnail)
    {
        if (original.Status == ShotAssetStatus.Corrupt
            || thumbnail.Status == ShotAssetStatus.Corrupt)
        {
            return Corrupt(original.Warning ?? thumbnail.Warning ?? "图片文件损坏");
        }

        return Missing();
    }

    private static ShotAssetReadResult Missing() => new(
        ShotAssetStatus.Missing,
        ReadOnlyMemory<byte>.Empty,
        "原图和缩略图文件都不存在");

    private static ShotAssetReadResult Corrupt(string warning) => new(
        ShotAssetStatus.Corrupt,
        ReadOnlyMemory<byte>.Empty,
        warning);
}
