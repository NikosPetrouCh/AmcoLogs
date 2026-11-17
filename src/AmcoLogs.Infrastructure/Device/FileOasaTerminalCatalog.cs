using System.Text.Json;
using System.Text.RegularExpressions;
using AmcoLogs.Core.Device;

namespace AmcoLogs.Infrastructure.Device;

public sealed class FileOasaTerminalCatalog : IOasaTerminalCatalog
{
    private readonly Dictionary<string, OasaTerminalHit> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OasaTerminalHit> _byDigits = new(StringComparer.Ordinal);

    public FileOasaTerminalCatalog(string? dataDirectory = null)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            roots.Add(dataDirectory);
        }

        roots.Add(Path.Combine(AppContext.BaseDirectory, "data"));
        foreach (var root in roots)
        {
            var path = Path.Combine(root, "Ips_Oasa.json");
            if (!File.Exists(path))
            {
                continue;
            }

            Load(path);
            if (_byKey.Count > 0)
            {
                break;
            }
        }
    }

    public int Count => _byKey.Count;

    public OasaTerminalHit? Find(string? terminal)
    {
        var key = Normalize(terminal);
        if (key.Length == 0)
        {
            return null;
        }

        if (_byKey.TryGetValue(key, out var hit))
        {
            return hit;
        }

        var digits = Digits(key);
        if (digits.Length == 0)
        {
            return null;
        }

        return _byDigits.TryGetValue(digits, out hit) ? hit : null;
    }

    private void Load(string path)
    {
        var json = Regex.Replace(File.ReadAllText(path), @"\}\s*\{", "},{");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in document.RootElement.EnumerateArray())
            {
                Add(item);
            }

            return;
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            Add(document.RootElement);
        }
    }

    private void Add(JsonElement item)
    {
        var terminal = Read(item, "terminal", "oasa", "oasaTerminal", "code");
        var ctvm = Read(item, "ctvm", "CTVM", "serviceAccountId", "serviceAccount");
        var ip = Read(item, "ip", "IP", "ipAddress");
        var station = Read(item, "station", "stationName", "name", "stop");
        if (string.IsNullOrWhiteSpace(terminal) || string.IsNullOrWhiteSpace(ctvm) || string.IsNullOrWhiteSpace(ip))
        {
            return;
        }

        var hit = new OasaTerminalHit
        {
            Terminal = terminal.Trim(),
            ServiceAccountId = ctvm.Trim(),
            IpAddress = ip.Trim(),
            Station = string.IsNullOrWhiteSpace(station) ? null : station.Trim()
        };
        _byKey[Normalize(terminal)] = hit;
        var digits = Digits(terminal);
        if (digits.Length > 0)
        {
            _byDigits[digits] = hit;
        }
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

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string Digits(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.TrimStart('0');
    }
}
