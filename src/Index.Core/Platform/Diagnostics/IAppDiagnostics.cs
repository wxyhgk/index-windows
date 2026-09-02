namespace Index.Platform.Diagnostics;

public enum AppDiagnosticLevel
{
    Trace,
    Information,
    Warning,
    Error,
}

/// <summary>
/// Receives structured, best-effort application diagnostics.
/// </summary>
/// <remarks>
/// Potentially sensitive values, such as window titles, should be supplied as named properties
/// instead of being embedded in <c>eventName</c>. Platform implementations can then
/// apply their privacy policy before persisting the event.
/// </remarks>
public interface IAppDiagnostics
{
    void Write(
        AppDiagnosticLevel level,
        string category,
        string eventName,
        IReadOnlyDictionary<string, string?>? properties = null,
        Exception? exception = null);
}

public sealed class NullAppDiagnostics : IAppDiagnostics
{
    public static NullAppDiagnostics Instance { get; } = new();

    private NullAppDiagnostics()
    {
    }

    public void Write(
        AppDiagnosticLevel level,
        string category,
        string eventName,
        IReadOnlyDictionary<string, string?>? properties = null,
        Exception? exception = null)
    {
    }
}
