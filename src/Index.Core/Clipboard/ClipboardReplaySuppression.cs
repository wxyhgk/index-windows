namespace Index.Clipboard;

/// <summary>
/// Short-lived guard for clipboard writes made while replaying a history item.
/// Windows may publish more than one sequence change for SetContent + Flush, so all
/// matching notifications inside the window are ignored.
/// </summary>
public sealed class ClipboardReplaySuppression
{
    private readonly object _gate = new();
    private readonly TimeSpan _lifetime;
    private string? _contentHash;
    private DateTimeOffset _expiresAt;

    public ClipboardReplaySuppression(TimeSpan? lifetime = null)
    {
        _lifetime = lifetime ?? TimeSpan.FromSeconds(3);
    }

    public void Mark(string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        lock (_gate)
        {
            _contentHash = contentHash;
            _expiresAt = DateTimeOffset.UtcNow + _lifetime;
        }
    }

    public bool ShouldSuppress(string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        lock (_gate)
        {
            if (_contentHash is not null && DateTimeOffset.UtcNow <= _expiresAt)
            {
                if (StringComparer.Ordinal.Equals(_contentHash, contentHash))
                    return true;
            }

            _contentHash = null;
            _expiresAt = default;
            return false;
        }
    }
}
