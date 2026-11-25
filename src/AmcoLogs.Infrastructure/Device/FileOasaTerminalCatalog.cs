
using System.Net;
using System.Text.Json;
using AmcoLogs.Core.Device;

namespace AmcoLogs.Infrastructure.Device;

public sealed class FileOasaTerminalCatalog : IOasaTerminalCatalog
{
    private readonly Dictionary<string, OasaTerminalHit> _byServiceAccountId =
        new(StringComparer.Ordinal);

    public FileOasaTerminalCatalog(string? dataDirectory = null)
    {
        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            roots.Add(dataDirectory);
        }

        roots.Add(Path.Combine(AppContext.BaseDirectory, "data"));

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var files = new[]
            {
        "Ips_Oasa_Metro.json",
        "Ips_Oasa_Tram.json"
    };

            foreach (var file in files)
            {
                var path = Path.Combine(root, file);

                if (File.Exists(path))
                {
                    Load(path);
                }
            }

            if (_byServiceAccountId.Count > 0)
            {
                break;
            }
        }
    }

    public int Count => _byServiceAccountId.Count;

    public OasaTerminalHit? Find(string? serviceAccountId)
    {
        var key = NormalizeServiceAccountId(serviceAccountId);

        if (key.Length == 0)
        {
            return null;
        }

        return _byServiceAccountId.TryGetValue(key, out var hit)
            ? hit
            : null;
    }

    private void Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                Add(item);
            }

            return;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            Add(root);
        }
    }

    private void Add(JsonElement item)
    {
        var serviceAccountId = Read(
            item,
            "serviceAccountId",
            "serviceAccount",
            "ServiceAccountId");

        var ctvmNumber = Read(
            item,
            "ctvmNumber",
            "CTVMNumber",
            "ctvm",
            "CTVM");

        var ip = Read(
            item,
            "routerIp",
            "RouterIp",
            "ipAddress",
            "ip",
            "IP");

        var station = Read(
            item,
            "station",
            "stationName",
            "name",
            "stop");

        if (string.IsNullOrWhiteSpace(serviceAccountId)
            || string.IsNullOrWhiteSpace(ctvmNumber)
            || string.IsNullOrWhiteSpace(ip))
        {
            return;
        }

        var normalizedId = NormalizeServiceAccountId(serviceAccountId);

        if (normalizedId.Length == 0)
        {
            return;
        }

        if (!DeviceCode.IsCtvmCode(ctvmNumber))
        {
            return;
        }

        if (!IPAddress.TryParse(ip.Trim(), out _))
        {
            return;
        }

        var hit = new OasaTerminalHit
        {
            ServiceAccountId = normalizedId,
            CtvmNumber = NormalizeCtvm(ctvmNumber),
            IpAddress = ip.Trim(),
            Station = string.IsNullOrWhiteSpace(station)
                ? null
                : station.Trim()
        };

        _byServiceAccountId[normalizedId] = hit;
    }

    private static string? Read(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetRawText();
            }
        }

        return null;
    }

    private static string NormalizeServiceAccountId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Trim().Where(char.IsDigit).ToArray());
    }

    private static string NormalizeCtvm(string value)
    {
        var digits = DeviceCode.Digits(value);

        return "CTVM" + digits;
    }
}
