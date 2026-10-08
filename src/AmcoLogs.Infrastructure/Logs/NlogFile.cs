using System.IO.Compression;
using System.Text.RegularExpressions;

namespace AmcoLogs.Infrastructure.Logs;

public static class NlogFile
{
    private static readonly Regex DayPattern = new(@"nlog-all-(\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> Enumerate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        if (Directory.Exists(path))
        {
            return Directory.GetFiles(path)
                .Where(IsNlogName)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return File.Exists(path) ? [path] : [];
    }

    public static string? DayFromName(string path)
    {
        var match = DayPattern.Match(Path.GetFileName(path));
        return match.Success ? match.Groups[1].Value : null;
    }

    public static Stream OpenRead(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return OpenZip(path);
        }

        return File.OpenRead(path);
    }

    private static Stream OpenZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.Entries.FirstOrDefault(item =>
            item.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            || item.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidOperationException($"No .log inside {Path.GetFileName(path)}.");
        }

        var temp = Path.Combine(Path.GetTempPath(), "amco-nlog-" + Guid.NewGuid().ToString("N") + ".log");
        entry.ExtractToFile(temp, overwrite: true);
        return new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
    }

    private static bool IsNlogName(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("nlog-all-", StringComparison.OrdinalIgnoreCase)
            && (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
    }
}
