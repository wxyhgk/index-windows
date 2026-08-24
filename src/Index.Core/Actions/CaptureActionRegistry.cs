namespace Index.Actions;

/// <summary>动作的唯一登记与查询入口；注册顺序即展示顺序。</summary>
public sealed class CaptureActionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ICaptureAction> _actions = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    public void Register(ICaptureAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (string.IsNullOrWhiteSpace(action.Descriptor.Id))
            throw new ArgumentException("动作 ID 不能为空。", nameof(action));

        lock (_gate)
        {
            if (!_actions.ContainsKey(action.Descriptor.Id))
                _order.Add(action.Descriptor.Id);
            _actions[action.Descriptor.Id] = action;
        }
    }

    public ICaptureAction? Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_gate)
            return _actions.GetValueOrDefault(id);
    }

    public IReadOnlyList<CaptureActionDescriptor> Descriptors(CaptureActionScope scope)
    {
        lock (_gate)
        {
            return _order
                .Select(id => _actions[id].Descriptor)
                .Where(descriptor => descriptor.Scopes.Contains(scope))
                .ToArray();
        }
    }
}
