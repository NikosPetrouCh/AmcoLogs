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

        var command = $"ping {ip}";
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ping,
                Arguments = ip,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20_000))
            {
                process.Kill(entireProcessTree: true);
                return Fail(target, ip, command + " · timeout");
            }

            if (process.ExitCode == 0)
            {
                return new SshPingResult
                {
                    ServiceAccountId = target.ServiceAccountId,
                    IpAddress = ip,
                    Reachable = true,
                    Message = command
                };
            }

            var detail = FirstLine(output.GetAwaiter().GetResult() + "\n" + error.GetAwaiter().GetResult());
            return Fail(target, ip, string.IsNullOrWhiteSpace(detail) ? command : command + " · " + detail);
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

    private static string FirstLine(string value)
    {
        var line = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
        return line.Length > 240 ? line[..240] : line;
    }

    private static string? FindPing()
    {
        var ping = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe");
        return File.Exists(ping) ? ping : null;
    }
}
