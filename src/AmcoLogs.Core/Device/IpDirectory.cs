using AmcoLogs.Core.Persistence;

namespace AmcoLogs.Core.Device;

public static class IpDirectory
{
    public static DeviceIpHit? Combine(string serviceAccountId, DeviceIpHit? fromJson, DeviceIpMapping? fromStore)
    {
        var id = serviceAccountId.Trim();
        var manual = IsManual(fromStore);
        var ip = manual && !string.IsNullOrWhiteSpace(fromStore?.IpAddress)
            ? fromStore!.IpAddress
            : FirstNonEmpty(fromJson?.IpAddress, fromStore?.IpAddress);
        var bus = manual && !string.IsNullOrWhiteSpace(fromStore?.BusNo)
            ? fromStore!.BusNo
            : FirstNonEmpty(fromJson?.BusNo, fromStore?.BusNo);

        if (string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }

        var mobility = FirstNonEmpty(fromJson?.Mobility, fromStore?.Mobility, DeviceCode.Classify(id).ToString())
            ?? "Unknown";
        var source = manual
            ? "manual"
            : fromJson?.Source ?? fromStore?.Source ?? "ip-file";

        return new DeviceIpHit
        {
            ServiceAccountId = id,
            IpAddress = ip.Trim(),
            BusNo = string.IsNullOrWhiteSpace(bus) ? null : bus.Trim(),
            Mobility = mobility,
            Source = source
        };
    }

    public static DeviceIpMapping? FindStored(IReadOnlyList<DeviceIpMapping> stored, string serviceAccountId)
    {
        var digits = DeviceCode.Digits(serviceAccountId);
        if (string.IsNullOrWhiteSpace(digits))
        {
            return null;
        }

        return stored.FirstOrDefault(item =>
            string.Equals(DeviceCode.Digits(item.ServiceAccountId), digits, StringComparison.Ordinal));
    }

    public static IReadOnlyList<DeviceIpMapping> Union(
        IEnumerable<DeviceIpHit> catalog,
        IEnumerable<DeviceIpMapping> stored)
    {
        var json = catalog.ToList();
        var db = stored.ToList();
        var keys = json.Select(item => DeviceCode.Digits(item.ServiceAccountId))
            .Concat(db.Select(item => DeviceCode.Digits(item.ServiceAccountId)))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var results = new List<DeviceIpMapping>();
        foreach (var digits in keys)
        {
            var hit = json.FirstOrDefault(item =>
                string.Equals(DeviceCode.Digits(item.ServiceAccountId), digits, StringComparison.Ordinal));
            var row = db.FirstOrDefault(item =>
                string.Equals(DeviceCode.Digits(item.ServiceAccountId), digits, StringComparison.Ordinal));
            var id = hit?.ServiceAccountId ?? row?.ServiceAccountId ?? digits;
            var combined = Combine(id, hit, row);
            if (combined is null)
            {
                continue;
            }

            results.Add(new DeviceIpMapping
            {
                ServiceAccountId = combined.ServiceAccountId,
                IpAddress = combined.IpAddress,
                BusNo = combined.BusNo,
                Mobility = combined.Mobility,
                Source = combined.Source,
                UpdatedAt = row?.UpdatedAt ?? DateTime.UtcNow
            });
        }

        return results
            .OrderBy(item => item.Mobility)
            .ThenBy(item => item.ServiceAccountId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsManual(DeviceIpMapping? mapping) =>
        string.Equals(mapping?.Source, "manual", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
