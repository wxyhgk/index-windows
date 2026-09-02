namespace Index.Storage;

public interface IShotDeletionRepository
{
    Task<bool> DeleteAsync(
        long shotId,
        CancellationToken cancellationToken = default);
}

public interface IShotOrganizationRepository
{
    Task<bool> IsFavoriteAsync(
        long shotId,
        CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(
        long shotId,
        bool isFavorite,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetTagsAsync(
        long shotId,
        CancellationToken cancellationToken = default);
}
