namespace Index.Platform;

/// <summary>Opens an absolute URI through a platform-owned shell adapter.</summary>
public interface IExternalUriOpener
{
    void Open(Uri uri);
}
