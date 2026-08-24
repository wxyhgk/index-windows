namespace Index.Recognition;

public interface IRecognitionPluginRegistry
{
    IReadOnlyList<RecognitionPluginDescriptor> Plugins { get; }

    IRecognitionPlugin<TOutput>? Find<TOutput>(
        RecognitionCapability capability,
        string? preferredPluginId = null);

    IRecognitionPlugin<TOutput> GetRequired<TOutput>(
        RecognitionCapability capability,
        string? preferredPluginId = null);
}

public sealed class RecognitionPluginRegistry : IRecognitionPluginRegistry, IDisposable
{
    private readonly IReadOnlyList<IRecognitionPlugin> _plugins;
    private bool _disposed;

    public RecognitionPluginRegistry(IEnumerable<IRecognitionPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        var registered = plugins.ToArray();
        var duplicate = registered
            .GroupBy(plugin => plugin.Descriptor.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Recognition plugin id '{duplicate.Key}' is registered more than once.",
                nameof(plugins));
        }

        foreach (var plugin in registered)
        {
            ValidateDescriptor(plugin.Descriptor);
        }

        _plugins = registered;
        Plugins = registered
            .Select(plugin => plugin.Descriptor)
            .OrderBy(descriptor => descriptor.Capability.Value, StringComparer.Ordinal)
            .ThenByDescending(descriptor => descriptor.Priority)
            .ThenBy(descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<RecognitionPluginDescriptor> Plugins { get; }

    public IRecognitionPlugin<TOutput>? Find<TOutput>(
        RecognitionCapability capability,
        string? preferredPluginId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var candidates = _plugins
            .OfType<IRecognitionPlugin<TOutput>>()
            .Where(plugin => plugin.Descriptor.Capability == capability);

        if (!string.IsNullOrWhiteSpace(preferredPluginId))
        {
            return candidates.FirstOrDefault(plugin => string.Equals(
                plugin.Descriptor.Id,
                preferredPluginId,
                StringComparison.OrdinalIgnoreCase));
        }

        return candidates
            .OrderByDescending(plugin => plugin.Descriptor.Priority)
            .ThenBy(plugin => plugin.Descriptor.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public IRecognitionPlugin<TOutput> GetRequired<TOutput>(
        RecognitionCapability capability,
        string? preferredPluginId = null) =>
        Find<TOutput>(capability, preferredPluginId)
        ?? throw new InvalidOperationException(
            $"No recognition plugin is registered for capability '{capability}' " +
            $"with output '{typeof(TOutput).FullName}'.");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var disposable in _plugins.OfType<IDisposable>())
        {
            disposable.Dispose();
        }
    }

    private static void ValidateDescriptor(RecognitionPluginDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Id))
        {
            throw new ArgumentException("Recognition plugin id cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
        {
            throw new ArgumentException(
                $"Recognition plugin '{descriptor.Id}' must have a display name.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.Capability.Value))
        {
            throw new ArgumentException(
                $"Recognition plugin '{descriptor.Id}' must declare a capability.");
        }
    }
}
