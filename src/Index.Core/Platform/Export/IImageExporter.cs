namespace Index.Platform.Export;

public sealed record ImageExportResult(string Path);

/// <summary>把已编码的 PNG 交给平台保存。</summary>
public interface IImageExporter
{
    ValueTask<ImageExportResult> ExportPngAsync(
        ReadOnlyMemory<byte> png,
        string? suggestedName = null,
        CancellationToken cancellationToken = default);
}
