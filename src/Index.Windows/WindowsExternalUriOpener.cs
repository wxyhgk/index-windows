using System.Diagnostics;

namespace Index.Platform;

public sealed class WindowsExternalUriOpener : IExternalUriOpener
{
    public void Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("An absolute URI is required.", nameof(uri));

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
    }
}
