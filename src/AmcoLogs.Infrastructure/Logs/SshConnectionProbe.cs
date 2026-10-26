using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AmcoLogs.Core.Logs;

namespace AmcoLogs.Infrastructure.Logs;

public sealed class SshConnectionProbe
{
    public const int DirectPort = 8022;
    public const int TunnelPort = 1030;

    private readonly DeviceAccessSettings _settings;

    public SshConnectionProbe(DeviceAccessSettings settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<SshPingResult>> PingAsync(
        IReadOnlyList<SshPingTarget> targets,
        CancellationToken cancellationToken = default)
    {
        var ssh = FindSsh();
        if (ssh is null)
        {
            return targets.Select(target => new SshPingResult
            {
                ServiceAccountId = target.ServiceAccountId,
                IpAddress = target.IpAddress.Trim(),
                Reachable = false,
                Message = "Δεν βρέθηκε το ssh.exe. Χρειάζεται OpenSSH Client."
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
                results[index] = await Task.Run(() => ProbeOne(targets[index], ssh), token);
            });

        return results;
    }

    private SshPingResult ProbeOne(SshPingTarget target, string ssh)
    {
        var ip = target.IpAddress.Trim();
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return Fail(target, ip, "Η IP δεν είναι έγκυρη διεύθυνση IPv4.");
        }

        var user = string.IsNullOrWhiteSpace(_settings.Username) ? "amco" : _settings.Username.Trim();
        if (user.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '.' and not '_' and not '-'))
        {
            return Fail(target, ip, "Ο χρήστης SSH δεν είναι έγκυρος.");
        }

        var direct = TryPort(ssh, user, ip, DirectPort);
        if (direct.Ok)
        {
            return new SshPingResult
            {
                ServiceAccountId = target.ServiceAccountId,
                IpAddress = ip,
                Reachable = true,
                Path = "direct",
                Port = DirectPort,
                Message = $"ssh {user}@{ip} -p{DirectPort} · απευθείας"
            };
        }

        var tunnel = TryPort(ssh, user, ip, TunnelPort);
        if (tunnel.Ok)
        {
            return new SshPingResult
            {
                ServiceAccountId = target.ServiceAccountId,
                IpAddress = ip,
                Reachable = true,
                Path = "tunnel",
                Port = TunnelPort,
                Message = $"ssh {user}@{ip} -p{DirectPort}: {direct.Detail}. ssh {user}@{ip} -p{TunnelPort} · tunnel"
            };
        }

        return Fail(target, ip, $"ssh {user}@{ip} -p{DirectPort}: {direct.Detail}. ssh {user}@{ip} -p{TunnelPort}: {tunnel.Detail}");
    }

    private static PortAttempt TryPort(string ssh, string user, string ip, int port)
    {
        var arguments = $"{user}@{ip} -p{port} -o BatchMode=yes -o ConnectTimeout=8 -o NumberOfPasswordPrompts=0 exit";
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ssh,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            if (!process.WaitForExit(12_000))
            {
                process.Kill(entireProcessTree: true);
                return new PortAttempt(false, "timeout");
            }

            var error = process.StandardError.ReadToEnd().Trim();
            if (process.ExitCode == 0)
            {
                return new PortAttempt(true, "σύνδεση");
            }

            var detail = string.IsNullOrWhiteSpace(error) ? $"exit {process.ExitCode}" : FirstLine(error);
            return new PortAttempt(false, detail);
        }
        catch (Exception exception)
        {
            return new PortAttempt(false, exception.Message);
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

    private readonly record struct PortAttempt(bool Ok, string Detail);

    private static string? FindSsh()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe"),
            "ssh"
        };

        foreach (var candidate in candidates)
        {
            if (candidate == "ssh" || File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
