using AmcoLogs.Core.Device;
using AmcoLogs.Core.Persistence;

namespace AmcoLogs.Infrastructure.Device;

public sealed class ManualIpCache
{
    private readonly Dictionary<string, DeviceIpMapping> _items = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void Upsert(DeviceIpMapping mapping)
    {
        var digits = DeviceCode.Digits(mapping.ServiceAccountId);
        if (string.IsNullOrWhiteSpace(digits))
        {
            return;
        }

        lock (_gate)
        {
            _items[digits] = mapping;
        }
    }

    public IReadOnlyList<DeviceIpMapping> All()
    {
        lock (_gate)
        {
            return _items.Values.ToArray();
        }
    }
}
