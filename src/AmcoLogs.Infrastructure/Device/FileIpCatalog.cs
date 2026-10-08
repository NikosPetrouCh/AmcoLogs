using System.Text.Json;
using System.Text.RegularExpressions;
using AmcoLogs.Core.Device;

namespace AmcoLogs.Infrastructure.Device;

public sealed class FileIpCatalog : IIpCatalog
{
    private readonly Dictionary<string, List<DeviceIpHit>> _byDigits = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public FileIpCatalog(string? dataDirectory = null)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            roots.Add(dataDirectory);
        }

        roots.Add(Path.Combine(AppContext.BaseDirectory, "data"));

        foreach (var root in roots)
        {
            LoadFile(Path.Combine(root, "Ips_TVM360.json"), "Fixed", "Ips_TVM360");
            LoadFile(Path.Combine(root, "Ips_Mobile.json"), "Mobile", "Ips_Mobile");
            if (_byDigits.Count > 0)
            {
                break;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _byDigits.Values.Sum(list => list.Count);
            }
        }
    }

    public DeviceIpHit? Find(string? serviceAccountId, string? mobility = null)
    {
        var digits = DeviceCode.Digits(serviceAccountId);
        if (string.IsNullOrWhiteSpace(digits))
        {
            return null;
        }

        lock (_gate)
        {
            if (!_byDigits.TryGetValue(digits, out var list) || list.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(mobility))
            {
                return list.FirstOrDefault(item =>
                    string.Equals(item.Mobility, mobility, StringComparison.OrdinalIgnoreCase)) ?? list[0];
            }

            return list[0];
        }
    }

    public IpResolveResult Resolve(IEnumerable<string> serviceAccountIds)
    {
        var hits = new List<DeviceIpHit>();
        var missing = new List<string>();
        foreach (var id in serviceAccountIds.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var hit = Find(id);
            if (hit is null)
            {
                missing.Add(id);
                continue;
            }

            hits.Add(hit with { ServiceAccountId = id.Trim() });
        }

        return new IpResolveResult
        {
            CatalogCount = Count,
            MatchedCount = hits.Count,
            Hits = hits,
            Missing = missing
        };
    }

    public IReadOnlyList<DeviceIpHit> All()
    {
        lock (_gate)
        {
            return _byDigits.Values.SelectMany(list => list).ToArray();
        }
    }

    private void LoadFile(string path, string mobility, string source)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var json = File.ReadAllText(path);
        json = Regex.Replace(json, @"\}\s*\{", "},{");
        using var document = JsonDocument.Parse(json);
        lock (_gate)
        {
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var ctvm = ReadString(item, "ctvm") ?? ReadString(item, "CTVM");
                    var ip = ReadString(item, "ip") ?? ReadString(item, "IP");
                    var bus = ReadString(item, "vehicle") ?? ReadString(item, "bus");
                    Add(ctvm, ip, bus, mobility, source);
                }
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    Add(property.Name, property.Value.GetString(), null, mobility, source);
                }
            }
        }
    }

    private void Add(string? ctvm, string? ip, string? bus, string mobility, string source)
    {
        if (string.IsNullOrWhiteSpace(ctvm) || string.IsNullOrWhiteSpace(ip))
        {
            return;
        }

        var digits = DeviceCode.Digits(ctvm);
        if (string.IsNullOrWhiteSpace(digits))
        {
            return;
        }

        var hit = new DeviceIpHit
        {
            ServiceAccountId = ctvm.Trim(),
            IpAddress = ip.Trim(),
            BusNo = string.IsNullOrWhiteSpace(bus) ? null : bus.Trim(),
            Mobility = mobility,
            Source = source
        };

        if (!_byDigits.TryGetValue(digits, out var list))
        {
            list = [];
            _byDigits[digits] = list;
        }

        list.Add(hit);
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
