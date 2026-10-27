using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AmcoLogs.Core.Logs;

namespace AmcoLogs.Infrastructure.Logs;

public sealed class SshConnectionProbe
{
    public async Task<IReadOnlyList<SshPingResult>> PingAsync(
        IReadOnlyList<SshPingTarget> targets,
        CancellationToken cancellationToken = default)
    {
        var ping = FindPing();
        if (ping is null)
        {
            return targets.Select(target => new SshPingResult
            {
                ServiceAccountId = target.ServiceAccountId,
                IpAddress = target.IpAddress.Trim(),
                Reachable = false,
                Message = "Δεν βρέθηκε το ping.exe."
            }).ToArray();
        }

        var results = new SshPingResult[targets.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, targets.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            },
            async (index, token) =>
            {
                results[index] = await Task.Run(() => ProbeOne(targets[index], ping), token);
            });

        return results;
    }

    private SshPingResult ProbeOne(SshPingTarget target, string ping)
    {
        var ip = target.IpAddress.Trim();
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return Fail(target, ip, "Η IP δεν είναι έγκυρη διεύθυνση IPv4.");
        }

        var command = $"ping -n 2 -w 1500 {ip}";
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ping,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("-n");
            process.StartInfo.ArgumentList.Add("2");
            process.StartInfo.ArgumentList.Add("-w");
            process.StartInfo.ArgumentList.Add("1500");
            process.StartInfo.ArgumentList.Add(ip);
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20_000))
            {
                process.Kill(entireProcessTree: true);
                return Fail(target, ip, command + " · timeout");
            }

            var text = outputTask.GetAwaiter().GetResult() + "\n" + errorTask.GetAwaiter().GetResult();
            var reply = EchoReply(text, ip);
            if (reply is not null)
            {
                return new SshPingResult
                {
                    ServiceAccountId = target.ServiceAccountId,
                    IpAddress = ip,
                    Reachable = true,
                    Message = reply
                };
            }

            return Fail(target, ip, FailureDetail(text));
        }
        catch (Exception exception)
        {
            return Fail(target, ip, command + " · " + exception.Message);
        }
    }

    private static SshPingResult Fail(SshPingTarget target, string ip, string message) => new()
    {
        ServiceAccountId = target.ServiceAccountId,
        IpAddress = ip,
        Reachable = false,
        Message = message
    };

    private static string? EchoReply(string text, string ip)
    {
        foreach (var line in Lines(text))
        {
            if (!line.Contains(ip, StringComparison.Ordinal) || !line.Contains("TTL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.Contains("unreachable", StringComparison.OrdinalIgnoreCase)
                || line.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || line.Contains("failure", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("Reply from " + ip, StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Απάντηση από " + ip, StringComparison.OrdinalIgnoreCase))
            {
                return Trim(line);
            }
        }

        return null;
    }

    private static string FailureDetail(string text)
    {
        var lines = Lines(text);
        var detail = lines.FirstOrDefault(line =>
            line.Contains("unreachable", StringComparison.OrdinalIgnoreCase)
            || line.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || line.Contains("General failure", StringComparison.OrdinalIgnoreCase)
            || line.Contains("transmit failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("could not find host", StringComparison.OrdinalIgnoreCase)
            || line.Contains("έληξε", StringComparison.OrdinalIgnoreCase)
            || line.Contains("αποτυχ", StringComparison.OrdinalIgnoreCase));
        return Trim(detail ?? lines.FirstOrDefault() ?? "Καμία απάντηση από τη συσκευή.");
    }

    private static List<string> Lines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

    private static string Trim(string value) => value.Length > 240 ? value[..240] : value;

    private static string? FindPing()
    {
        var ping = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe");
        return File.Exists(ping) ? ping : null;
    }
}
