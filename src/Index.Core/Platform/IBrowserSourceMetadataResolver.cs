namespace Index.Platform;

public interface IBrowserSourceMetadataResolver
{
    Task<string?> ResolveUrlAsync(
        SourceApplicationInfo? application,
        CancellationToken cancellationToken = default);
}
