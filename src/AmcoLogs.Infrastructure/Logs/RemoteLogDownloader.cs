using System.Diagnostics;
using System.Globalization;
using AmcoLogs.Core.Logs;

namespace AmcoLogs.Infrastructure.Logs;

public sealed class RemoteLogDownloader : ILogDownloadJob
{
    private readonly DeviceAccessSettings _settings;
    private readonly object _gate = new();
    private readonly List<string> _log = [];
    private readonly List<LogDownloadResult> _results = [];
    private bool _running;
    private string _phase = "Idle";

    public RemoteLogDownloader(DeviceAccessSettings settings)
    {
        _settings = settings;
    }

    public LogDownloadJobStatus GetStatus()
    {
        lock (_gate)
        {
            return new LogDownloadJobStatus
            {
                Running = _running,
                Phase = _phase,
                Log = _log.ToArray(),
                Results = _results.ToArray()
            };
        }
    }

    public Task<bool> StartAsync(IReadOnlyList<LogDownloadDeviceRequest> devices, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_running)
            {
                return Task.FromResult(false);
            }

            _running = true;
            _phase = "Starting";
            _log.Clear();
            _results.Clear();
        }

        _ = Task.Run(() => Run(devices), CancellationToken.None);
        return Task.FromResult(true);
    }

    private void Run(IReadOnlyList<LogDownloadDeviceRequest> devices)
    {
        try
        {
            var scp = FindOpenSsh("scp.exe");
            if (scp is null)
            {
                Log("scp not found. Install OpenSSH Client (scp.exe).");
                SetPhase("Error");
                return;
            }

            var ssh = devices.Any(device => IsMobile(device.Mobility)) ? FindOpenSsh("ssh.exe") : null;
            Log("Using " + scp);
            foreach (var device in devices)
            {
                SetPhase($"{device.ServiceAccountId} {device.IpAddress}");
                var result = DownloadOne(device, scp, ssh);
                lock (_gate)
                {
                    _results.Add(result);
                }
            }

            SetPhase("Done");
            Log("Finished scp download.");
        }
        catch (Exception exception)
        {
            Log("Download failed: " + exception.Message);
            SetPhase("Error");
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
            }
        }
    }

    private LogDownloadResult DownloadOne(LogDownloadDeviceRequest device, string scp, string? ssh)
    {
        var dates = NormalizeDates(device.LogDates);
        if (dates.Count == 0)
        {
            return Fail(device, "no-dates", "No TransactionDate in the Excel for this device.");
        }

        var mobile = IsMobile(device.Mobility);
        if (mobile && ssh is null)
        {
            return Fail(device, "error", "ssh not found. Install OpenSSH Client (ssh.exe).");
        }

        var user = string.IsNullOrWhiteSpace(_settings.Username) ? "amco" : _settings.Username;
        var port = _settings.Port <= 0 ? 8022 : _settings.Port;
        var remoteDir = string.IsNullOrWhiteSpace(_settings.RemoteLogPath)
            ? "/opt/amco/middleware/TVMserver"
            : _settings.RemoteLogPath.TrimEnd('/');
        var destRoot = ResolveLocalFolder();
        var destDir = Path.Combine(destRoot, Sanitize(device.ServiceAccountId));
        Directory.CreateDirectory(destDir);

        var removeFailed = false;
        try
        {
            foreach (var day in dates)
            {
                var logName = $"nlog-all-{day}.log";
                var zipName = $"nlog-all-{day}.zip";
                var remoteName = logName;
                if (mobile)
                {
                    var remoteCommand = $"cd {remoteDir} && sudo zip {zipName} {logName}";
                    var sshCommand = $"-p {port} {user}@{device.IpAddress} {Quote(remoteCommand)}";
                    Log($"ssh {sshCommand}");
                    RunProcess(ssh!, sshCommand, 300_000, closeInput: true);
                    remoteName = zipName;
                }

                var remoteFile = $"{remoteDir}/{remoteName}";
                var command = $"-P {port} {user}@{device.IpAddress}:{remoteFile} {Quote(destDir)}";
                Log($"scp {command}");
                RunProcess(scp, command, 180_000, closeInput: false);

                if (!mobile)
                {
                    continue;
                }

                try
                {
                    var removeCommand = $"cd {remoteDir} && sudo rm {zipName}";
                    var sshRemove = $"-p {port} {user}@{device.IpAddress} {Quote(removeCommand)}";
                    Log($"ssh {sshRemove}");
                    RunProcess(ssh!, sshRemove, 60_000, closeInput: true);
                }
                catch (Exception exception)
                {
                    removeFailed = true;
                    Log($"{device.ServiceAccountId}: το zip κατέβηκε, το sudo rm απέτυχε: {exception.Message}");
                }
            }

            var files = Directory.GetFiles(destDir, "nlog-all-*");
            var message = files.Length > 0
                ? $"Downloaded {files.Length} file(s) to {destDir}."
                : "scp finished but no nlog file landed in the folder.";
            if (removeFailed)
            {
                message += " Το zip έμεινε στη συσκευή γιατί το sudo rm απέτυχε.";
            }

            return new LogDownloadResult
            {
                ServiceAccountId = device.ServiceAccountId,
                IpAddress = device.IpAddress,
                Mobility = device.Mobility,
                Reachable = true,
                Status = files.Length > 0 ? "downloaded" : "empty",
                LocalPath = destDir,
                Message = message
            };
        }
        catch (Exception exception)
        {
            Log($"{device.ServiceAccountId}: {exception.Message}");
            return Fail(device, "error", exception.Message);
        }
    }

    private static void RunProcess(string fileName, string arguments, int timeoutMs, bool closeInput)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = closeInput,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.Start();
        if (closeInput)
        {
            process.StandardInput.Close();
        }

        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill(true);
            throw new TimeoutException(Path.GetFileName(fileName) + " timed out.");
        }

        var error = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"{Path.GetFileName(fileName)} exit {process.ExitCode}" : error.Trim());
        }
    }

    private static bool IsMobile(string mobility) =>
        string.Equals(mobility, "Mobile", StringComparison.OrdinalIgnoreCase);

    private string ResolveLocalFolder()
    {
        if (!string.IsNullOrWhiteSpace(_settings.LocalDownloadFolder))
        {
            Directory.CreateDirectory(_settings.LocalDownloadFolder);
            return _settings.LocalDownloadFolder;
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Amco files");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string? FindOpenSsh(string fileName)
    {
        var bare = Path.GetFileNameWithoutExtension(fileName);
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", fileName),
            bare
        };

        foreach (var candidate in candidates)
        {
            if (candidate == bare || File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> NormalizeDates(IEnumerable<string>? dates)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in dates ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                || DateTime.TryParse(raw, out parsed))
            {
                result.Add(parsed.ToString("yyyy-MM-dd"));
            }
            else if (raw.Length >= 10)
            {
                result.Add(raw[..10]);
            }
        }

        return [.. result];
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static LogDownloadResult Fail(LogDownloadDeviceRequest device, string status, string message) =>
        new()
        {
            ServiceAccountId = device.ServiceAccountId,
            IpAddress = device.IpAddress,
            Mobility = device.Mobility,
            Reachable = false,
            Status = status,
            Message = message
        };

    private void Log(string message)
    {
        lock (_gate)
        {
            _log.Add($"{DateTime.Now:HH:mm:ss} {message}");
            if (_log.Count > 120)
            {
                _log.RemoveAt(0);
            }
        }
    }

    private void SetPhase(string phase)
    {
        lock (_gate)
        {
            _phase = phase;
        }
    }
}
