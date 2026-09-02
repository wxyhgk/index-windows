using System.Text;
using System.Text.Json;
using Index.Platform.Diagnostics;

namespace Index.Platform;

/// <summary>
/// Writes bounded JSON-lines diagnostics to the current user's local application data directory.
/// Diagnostics are deliberately best-effort and never interrupt application work.
/// </summary>
public sealed class LocalAppDiagnostics : IAppDiagnostics
{
    public const long DefaultMaxFileBytes = 2 * 1024 * 1024;
    public const int DefaultMaxArchiveFiles = 3;

    private const int MinimumMaxFileBytes = 256;
    private const int MaxIdentityLength = 160;
    private const int MaxPropertyNameLength = 96;
    private const int MaxPropertyValueLength = 4096;
    private const int MaxExceptionLength = 16 * 1024;

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _logPath;
    private readonly long _maxFileBytes;
    private readonly int _maxArchiveFiles;
    private readonly bool _includeSensitiveData;
    private readonly TimeProvider _timeProvider;

    public LocalAppDiagnostics(
        string? logDirectory = null,
        long maxFileBytes = DefaultMaxFileBytes,
        int maxArchiveFiles = DefaultMaxArchiveFiles,
        bool includeSensitiveData = false,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, MinimumMaxFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxArchiveFiles);

        _directory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Index",
            "logs");
        _logPath = Path.Combine(_directory, "index.log");
        _maxFileBytes = maxFileBytes;
        _maxArchiveFiles = maxArchiveFiles;
        _includeSensitiveData = includeSensitiveData;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Write(
        AppDiagnosticLevel level,
        string category,
        string eventName,
        IReadOnlyDictionary<string, string?>? properties = null,
        Exception? exception = null)
    {
        try
        {
            byte[] line = CreateBoundedLine(level, category, eventName, properties, exception);

            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                RollIfNeeded(line.Length);

                using var stream = new FileStream(
                    _logPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);
                stream.Write(line);
            }
        }
        catch
        {
            // Diagnostics must never become a failure mode for the operation being diagnosed.
        }
    }

    private byte[] CreateBoundedLine(
        AppDiagnosticLevel level,
        string category,
        string eventName,
        IReadOnlyDictionary<string, string?>? properties,
        Exception? exception)
    {
        Dictionary<string, string?>? safeProperties = SanitizeProperties(properties);
        string? exceptionText = exception is null || !_includeSensitiveData
            ? null
            : Truncate(exception.ToString(), MaxExceptionLength);

        byte[] line = SerializeLine(
            level,
            Truncate(category, MaxIdentityLength),
            Truncate(eventName, MaxIdentityLength),
            safeProperties,
            exception?.GetType().FullName,
            exceptionText);
        if (line.Length <= _maxFileBytes)
            return line;

        line = SerializeLine(
            level,
            Truncate(category, 48),
            Truncate(eventName, 48),
            properties: null,
            exception?.GetType().FullName,
            exceptionText: null);
        if (line.Length <= _maxFileBytes)
            return line;

        return SerializeLine(
            level,
            "diagnostics",
            "event-truncated",
            properties: null,
            exceptionType: null,
            exceptionText: null);
    }

    private byte[] SerializeLine(
        AppDiagnosticLevel level,
        string category,
        string eventName,
        IReadOnlyDictionary<string, string?>? properties,
        string? exceptionType,
        string? exceptionText)
    {
        var entry = new LogEntry(
            _timeProvider.GetUtcNow(),
            level.ToString(),
            category,
            eventName,
            properties,
            exceptionType,
            exceptionText);
        string json = JsonSerializer.Serialize(entry);
        return Encoding.UTF8.GetBytes(json + Environment.NewLine);
    }

    private Dictionary<string, string?>? SanitizeProperties(
        IReadOnlyDictionary<string, string?>? properties)
    {
        if (properties is null || properties.Count == 0)
            return null;

        var result = new Dictionary<string, string?>(properties.Count, StringComparer.Ordinal);
        foreach ((string key, string? value) in properties)
        {
            string safeKey = Truncate(key, MaxPropertyNameLength);
            result[safeKey] = !_includeSensitiveData && IsSensitiveTitleKey(key)
                ? "[redacted]"
                : value is null ? null : Truncate(value, MaxPropertyValueLength);
        }

        return result;
    }

    private static bool IsSensitiveTitleKey(string key) =>
        key.Contains("title", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("caption", StringComparison.OrdinalIgnoreCase);

    private void RollIfNeeded(int incomingBytes)
    {
        if (!File.Exists(_logPath))
            return;

        long existingBytes = new FileInfo(_logPath).Length;
        if (existingBytes + incomingBytes <= _maxFileBytes)
            return;

        if (_maxArchiveFiles == 0)
        {
            File.Delete(_logPath);
            return;
        }

        string oldestArchive = ArchivePath(_maxArchiveFiles);
        if (File.Exists(oldestArchive))
            File.Delete(oldestArchive);

        for (int index = _maxArchiveFiles - 1; index >= 1; index--)
        {
            string source = ArchivePath(index);
            if (File.Exists(source))
                File.Move(source, ArchivePath(index + 1));
        }

        File.Move(_logPath, ArchivePath(1));
    }

    private string ArchivePath(int index) => Path.Combine(_directory, $"index.{index}.log");

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record LogEntry(
        DateTimeOffset TimestampUtc,
        string Level,
        string Category,
        string EventName,
        IReadOnlyDictionary<string, string?>? Properties,
        string? ExceptionType,
        string? Exception);
}
