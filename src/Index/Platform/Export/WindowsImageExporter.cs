namespace Index.Platform.Export;

/// <summary>Windows 默认 PNG 导出器：异步写入目录并以 CreateNew 保证不覆盖。</summary>
public sealed class WindowsImageExporter : IImageExporter
{
    private readonly string _directory;

    public WindowsImageExporter(string? directory = null)
    {
        _directory = Path.GetFullPath(directory ??
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
    }

    public async ValueTask<ImageExportResult> ExportPngAsync(
        ReadOnlyMemory<byte> png,
        string? suggestedName = null,
        CancellationToken cancellationToken = default)
    {
        if (png.IsEmpty)
            throw new ArgumentException("PNG 数据不能为空。", nameof(png));

        Directory.CreateDirectory(_directory);
        string baseName = NormalizeBaseName(suggestedName);

        for (int suffix = 0; ; suffix++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = suffix == 0
                ? $"{baseName}.png"
                : $"{baseName} ({suffix}).png";
            string path = Path.Combine(_directory, fileName);

            FileStream? stream = null;
            try
            {
                stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await stream.WriteAsync(png, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                return new ImageExportResult(path);
            }
            catch (IOException) when (stream is null && File.Exists(path))
            {
                // 名称竞争或文件已存在，尝试下一个后缀。
            }
            catch
            {
                if (stream is not null)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    stream = null;
                    TryDeleteIncomplete(path);
                }
                throw;
            }
            finally
            {
                if (stream is not null)
                    await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static string NormalizeBaseName(string? suggestedName)
    {
        string candidate = string.IsNullOrWhiteSpace(suggestedName)
            ? $"Index_{DateTime.Now:yyyyMMdd_HHmmss_fff}"
            : Path.GetFileNameWithoutExtension(Path.GetFileName(suggestedName.Trim()));

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        candidate = new string(candidate.Where(character => !invalid.Contains(character)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(candidate)
            ? $"Index_{DateTime.Now:yyyyMMdd_HHmmss_fff}"
            : candidate;
    }

    private static void TryDeleteIncomplete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // 只清理由本次 CreateNew 创建的未完成文件；清理失败保留原异常。
        }
    }
}
