using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SkiaSharp;

const string SourcePrefix = "index://performance-seed/v1/";
const string SeedAppName = "Index Performance Seed";
const string SeedAppIdentifier = "index.performance.seed";

var options = SeederOptions.Parse(args);
var root = Path.GetFullPath(options.RootDirectory);
var originals = Path.Combine(root, "originals");
var thumbnails = Path.Combine(root, "thumbnails");
Directory.CreateDirectory(originals);
Directory.CreateDirectory(thumbnails);

var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = Path.Combine(root, "index.sqlite"),
    Mode = SqliteOpenMode.ReadWrite,
    Cache = SqliteCacheMode.Shared,
    Pooling = false
}.ToString();

await using var connection = new SqliteConnection(connectionString);
await connection.OpenAsync();
await ExecuteAsync(connection, "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 10000; PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;");

var existing = await LoadExistingAsync(connection, options.Count);
var startedAt = DateTimeOffset.UtcNow;
var capturedBase = startedAt.AddMilliseconds(-options.Count);
var inserted = 0;
var repaired = 0;
var skipped = 0;

Console.WriteLine($"Index performance seed: target={options.Count:N0}, existing={existing.Count:N0}, root={root}");

for (var batchStart = 1; batchStart <= options.Count; batchStart += options.BatchSize)
{
    var batchEnd = Math.Min(options.Count, batchStart + options.BatchSize - 1);
    var assets = new SeedAsset[batchEnd - batchStart + 1];
    Parallel.For(
        0,
        assets.Length,
        new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism },
        index =>
        {
            var number = batchStart + index;
            if (existing.TryGetValue(number, out var current))
            {
                var currentOriginal = Path.Combine(originals, $"{current.Sha256}.png");
                if (File.Exists(currentOriginal))
                {
                    CreateThumbnailLinkIfMissing(
                        Path.Combine(thumbnails, $"{current.Sha256}.jpg"),
                        currentOriginal);
                    assets[index] = new SeedAsset(number, current.Sha256, current, IsVerifiedExisting: true);
                    return;
                }
            }

            var png = SeedImageRenderer.Render(number);
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(png));
            var originalPath = Path.Combine(originals, $"{sha256}.png");
            var thumbnailPath = Path.Combine(thumbnails, $"{sha256}.jpg");
            WriteAtomicIfMissing(originalPath, png);
            CreateThumbnailLinkIfMissing(thumbnailPath, originalPath);
            assets[index] = new SeedAsset(number, sha256, current, IsVerifiedExisting: false);
        });

    await using var transaction = await connection.BeginTransactionAsync();
    try
    {
        foreach (var asset in assets)
        {
            if (asset.Existing is { } row)
            {
                if (!asset.IsVerifiedExisting
                    && !StringComparer.Ordinal.Equals(row.Sha256, asset.Sha256))
                {
                    await UpdateExistingAsync(
                        connection,
                        transaction,
                        row.Id,
                        asset.Sha256,
                        asset.Number);
                    repaired++;
                }
                else
                {
                    skipped++;
                }
                continue;
            }

            var capturedAt = capturedBase.AddMilliseconds(asset.Number);
            var shotId = await InsertShotAsync(
                connection,
                transaction,
                asset.Sha256,
                asset.Number,
                capturedAt);
            await InsertRevisionAsync(connection, transaction, shotId, capturedAt);
            inserted++;
        }
        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }

    var completed = batchEnd;
    var elapsed = DateTimeOffset.UtcNow - startedAt;
    var rate = completed / Math.Max(elapsed.TotalSeconds, 0.001);
    Console.WriteLine($"{completed,7:N0}/{options.Count:N0}  inserted={inserted:N0} repaired={repaired:N0} skipped={skipped:N0}  {rate:N0}/s");
}

var total = await ScalarLongAsync(
    connection,
    "SELECT COUNT(*) FROM shot WHERE sourceURL LIKE 'index://performance-seed/v1/%'");
var duration = DateTimeOffset.UtcNow - startedAt;
Console.WriteLine($"Done: seedRows={total:N0}, inserted={inserted:N0}, repaired={repaired:N0}, elapsed={duration}");

static async Task<Dictionary<int, ExistingSeedRow>> LoadExistingAsync(
    SqliteConnection connection,
    int maximum)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT id, sha256, sourceURL
        FROM shot
        WHERE sourceURL LIKE 'index://performance-seed/v1/%'
        """;
    var result = new Dictionary<int, ExistingSeedRow>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var source = reader.GetString(2);
        if (!int.TryParse(source.AsSpan(SourcePrefix.Length), out var number)
            || number < 1
            || number > maximum)
            continue;
        result.TryAdd(number, new ExistingSeedRow(reader.GetInt64(0), reader.GetString(1)));
    }
    return result;
}

static async Task<long> InsertShotAsync(
    SqliteConnection connection,
    System.Data.Common.DbTransaction transaction,
    string sha256,
    int number,
    DateTimeOffset capturedAt)
{
    await using var command = connection.CreateCommand();
    command.Transaction = (SqliteTransaction)transaction;
    command.CommandText = """
        INSERT INTO shot(
            sha256, capturedAt, pixelWidth, pixelHeight, scale,
            appName, appIdentifier, windowTitle, sourceURL,
            displayIndex, displayName, regionX, regionY,
            regionWidth, regionHeight, originalExtension)
        VALUES(
            $sha256, $capturedAt, 320, 180, 1.0,
            $appName, $appIdentifier, $windowTitle, $sourceURL,
            0, 'Performance Seed', 0, 0, 320, 180, 'png')
        RETURNING id
        """;
    command.Parameters.AddWithValue("$sha256", sha256);
    command.Parameters.AddWithValue("$capturedAt", capturedAt.ToString("O"));
    command.Parameters.AddWithValue("$appName", SeedAppName);
    command.Parameters.AddWithValue("$appIdentifier", SeedAppIdentifier);
    command.Parameters.AddWithValue("$windowTitle", $"Performance image {number:N0}");
    command.Parameters.AddWithValue("$sourceURL", SourcePrefix + number);
    return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
}

static async Task InsertRevisionAsync(
    SqliteConnection connection,
    System.Data.Common.DbTransaction transaction,
    long shotId,
    DateTimeOffset capturedAt)
{
    await using var command = connection.CreateCommand();
    command.Transaction = (SqliteTransaction)transaction;
    command.CommandText = """
        INSERT INTO revision(shotID, parentID, createdAt, note, layersJSON)
        VALUES($shotID, NULL, $createdAt, 'performance-seed', '[]')
        """;
    command.Parameters.AddWithValue("$shotID", shotId);
    command.Parameters.AddWithValue("$createdAt", capturedAt.ToString("O"));
    await command.ExecuteNonQueryAsync();
}

static async Task UpdateExistingAsync(
    SqliteConnection connection,
    System.Data.Common.DbTransaction transaction,
    long shotId,
    string sha256,
    int number)
{
    await using var command = connection.CreateCommand();
    command.Transaction = (SqliteTransaction)transaction;
    command.CommandText = """
        UPDATE shot
        SET sha256 = $sha256,
            pixelWidth = 320,
            pixelHeight = 180,
            appName = $appName,
            appIdentifier = $appIdentifier,
            windowTitle = $windowTitle
        WHERE id = $id
        """;
    command.Parameters.AddWithValue("$sha256", sha256);
    command.Parameters.AddWithValue("$appName", SeedAppName);
    command.Parameters.AddWithValue("$appIdentifier", SeedAppIdentifier);
    command.Parameters.AddWithValue("$windowTitle", $"Performance image {number:N0}");
    command.Parameters.AddWithValue("$id", shotId);
    await command.ExecuteNonQueryAsync();
}

static void WriteAtomicIfMissing(string target, byte[] bytes)
{
    if (File.Exists(target)) return;
    var stage = target + $".{Guid.NewGuid():N}.tmp";
    try
    {
        File.WriteAllBytes(stage, bytes);
        try { File.Move(stage, target, overwrite: false); }
        catch (IOException) when (File.Exists(target)) { }
    }
    finally
    {
        if (File.Exists(stage)) File.Delete(stage);
    }
}

static void CreateThumbnailLinkIfMissing(string thumbnailPath, string originalPath)
{
    if (File.Exists(thumbnailPath)) return;
    if (!NativeMethods.CreateHardLink(thumbnailPath, originalPath, IntPtr.Zero))
    {
        var error = Marshal.GetLastWin32Error();
        if (!File.Exists(thumbnailPath))
            throw new IOException($"Unable to create thumbnail hard link ({error}): {thumbnailPath}");
    }
}

static async Task ExecuteAsync(SqliteConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}

static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed record ExistingSeedRow(long Id, string Sha256);
internal sealed record SeedAsset(
    int Number,
    string Sha256,
    ExistingSeedRow? Existing,
    bool IsVerifiedExisting);

internal sealed record SeederOptions(
    string RootDirectory,
    int Count,
    int BatchSize,
    int Parallelism)
{
    public static SeederOptions Parse(string[] args)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Index");
        var count = 100_000;
        var batchSize = 1_000;
        var parallelism = Math.Clamp(Environment.ProcessorCount, 2, 12);
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--root": root = args[++index]; break;
                case "--count": count = int.Parse(args[++index]); break;
                case "--batch-size": batchSize = int.Parse(args[++index]); break;
                case "--parallelism": parallelism = int.Parse(args[++index]); break;
                default: throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (parallelism <= 0) throw new ArgumentOutOfRangeException(nameof(parallelism));
        return new SeederOptions(root, count, batchSize, parallelism);
    }
}

internal static class SeedImageRenderer
{
    private static readonly SKTypeface Typeface =
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);

    public static byte[] Render(int number)
    {
        const int width = 320;
        const int height = 180;
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        var hue = (uint)(number * 2654435761u);
        var background = new SKColor(
            (byte)(42 + (hue & 0x3F)),
            (byte)(48 + ((hue >> 8) & 0x3F)),
            (byte)(72 + ((hue >> 16) & 0x3F)));
        canvas.Clear(background);

        var label = number.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        using var font = new SKFont(Typeface, label.Length > 5 ? 54 : 64);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var textWidth = font.MeasureText(label);
        var metrics = font.Metrics;
        var x = (width - textWidth) / 2f;
        var y = (height - metrics.Ascent - metrics.Descent) / 2f;
        canvas.DrawText(label, x, y, SKTextAlign.Left, font, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 92)
            ?? throw new InvalidOperationException($"Unable to encode seed image {number}.");
        return encoded.ToArray();
    }
}

internal static partial class NativeMethods
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateHardLink(
        string newFileName,
        string existingFileName,
        IntPtr securityAttributes);
}
