namespace Index.Platform;

/// <summary>
/// A tiny ownership marker lets the next capture remove an MTT path left behind by a process
/// crash without ever touching a virtual display that the user activated independently.
/// </summary>
internal sealed class VirtualDisplayRecoveryJournal
{
    private readonly string _path;

    public VirtualDisplayRecoveryJournal(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Index",
            "virtual-display-capture.pending");
    }

    public bool IsPending => File.Exists(_path);

    public void MarkPending()
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(
            _path,
            $"pid={Environment.ProcessId}{Environment.NewLine}" +
            $"started={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
    }

    public void Clear()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
