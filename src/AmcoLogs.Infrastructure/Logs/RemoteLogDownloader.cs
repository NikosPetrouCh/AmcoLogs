using System.Globalization;
using AmcoLogs.Core.Logs;
using Renci.SshNet;

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
            var fixedCount = devices.Count(device => !IsMobile(device.Mobility));
            var mobileCount = devices.Count - fixedCount;
            Log($"Κατέβασμα για {devices.Count} συσκευές με επιτυχές ping: {fixedCount} σταθερές, {mobileCount} κινητές. Χωρίς νέο ping.");
            foreach (var device in devices)
            {
                SetPhase($"{device.ServiceAccountId} {device.IpAddress}");
                var result = DownloadOne(device);
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

    private LogDownloadResult DownloadOne(LogDownloadDeviceRequest device)
    {
        var dates = NormalizeDates(device.LogDates);
        if (dates.Count == 0)
        {
            return Fail(device, "no-dates", "No TransactionDate in the Excel for this device.");
        }

        var mobile = IsMobile(device.Mobility);
        Log(mobile
            ? $"{device.ServiceAccountId}: κινητή, zip και κατέβασμα."
            : $"{device.ServiceAccountId}: σταθερή, κατέβασμα του nlog.");

        var user = string.IsNullOrWhiteSpace(_settings.Username) ? "amco" : _settings.Username;
        var port = _settings.Port <= 0 ? 8022 : _settings.Port;
        var remoteDir = string.IsNullOrWhiteSpace(_settings.RemoteLogPath)
            ? "/opt/amco/middleware/TVMserver"
            : _settings.RemoteLogPath.TrimEnd('/');
        var destRoot = ResolveLocalFolder();
        var destDir = Path.Combine(destRoot, Sanitize(device.ServiceAccountId));
        Directory.CreateDirectory(destDir);

        var removeFailed = false;
        var expected = new List<string>();
        try
        {
            foreach (var day in dates)
            {
                var logName = $"nlog-all-{day}.log";
                var zipName = $"nlog-all-{day}.zip";
                var remoteName = logName;
                if (mobile)
                {
                    RunSudo(device.IpAddress, port, user, remoteDir, $"zip {zipName} {logName}", 300_000);
                    remoteName = zipName;
                }

                var remoteFile = $"{remoteDir}/{remoteName}";
                var localFile = Path.Combine(destDir, remoteName);
                Log($"scp -P {port} {user}@{device.IpAddress}:{remoteFile} \"{destDir}\"");
                DownloadFile(device.IpAddress, port, user, remoteFile, destDir, 180_000);
                if (!File.Exists(localFile))
                {
                    throw new InvalidOperationException($"Το {remoteName} δεν εμφανίστηκε στο {destDir}.");
                }

                expected.Add(localFile);
                if (!mobile)
                {
                    continue;
                }

                try
                {
                    RunSudo(device.IpAddress, port, user, remoteDir, $"rm {zipName}", 60_000);
                }
                catch (Exception exception)
                {
                    removeFailed = true;
                    Log($"{device.ServiceAccountId}: το zip κατέβηκε, το sudo rm απέτυχε: {exception.Message}");
                }
            }

            var message = $"Downloaded {expected.Count} file(s) to {destDir}.";
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
                Status = expected.Count > 0 ? "downloaded" : "empty",
                LocalPath = destDir,
                Message = message
            };
        }
        catch (Exception exception)
        {
            var message = Redact(exception.Message);
            Log($"{device.ServiceAccountId}: {message}");
            return Fail(device, "error", message);
        }
    }

    private void RunSudo(string ip, int port, string user, string remoteDir, string action, int timeoutMs)
    {
        var script = $"cd {remoteDir} && {action}";
        var password = _settings.Password ?? "";
        var command = string.IsNullOrEmpty(password)
            ? $"sudo sh -c \"{script}\""
            : $"echo {QuoteShell(password)} | sudo -S -p '' sh -c \"{script}\"";
        Log($"ssh -p {port} {user}@{ip} sudo -S sh -c \"{script}\"");
        using var client = Open<SshClient>(ip, port, user);
        client.Connect();
        try
        {
            using var cmd = client.CreateCommand(command);
            cmd.CommandTimeout = TimeSpan.FromMilliseconds(timeoutMs);
            var output = Redact((cmd.Execute() + "\n" + cmd.Error).Trim());
            if (!string.IsNullOrWhiteSpace(output))
            {
                Log(output);
            }

            if (cmd.ExitStatus != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(output) ? $"sudo exit {cmd.ExitStatus}" : output);
            }
        }
        finally
        {
            client.Disconnect();
        }
    }

    private void DownloadFile(string ip, int port, string user, string remoteFile, string destDir, int timeoutMs)
    {
        using var client = Open<ScpClient>(ip, port, user);
        client.OperationTimeout = TimeSpan.FromMilliseconds(timeoutMs);
        client.Connect();
        try
        {
            client.Download(remoteFile, new DirectoryInfo(destDir));
        }
        finally
        {
            client.Disconnect();
        }
    }

    private T Open<T>(string ip, int port, string user) where T : BaseClient
    {
        var password = _settings.Password ?? "";
        var keyboard = new KeyboardInteractiveAuthenticationMethod(user);
        keyboard.AuthenticationPrompt += (_, args) =>
        {
            foreach (var prompt in args.Prompts)
            {
                prompt.Response = password;
            }
        };
        var connection = new ConnectionInfo(
            ip,
            port,
            user,
            new PasswordAuthenticationMethod(user, password),
            keyboard)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        var client = (T)Activator.CreateInstance(typeof(T), connection)!;
        client.HostKeyReceived += (_, args) => args.CanTrust = true;
        return client;
    }

    private static string QuoteShell(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private string Redact(string text)
    {
        var password = _settings.Password;
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text.Replace(password, "***", StringComparison.Ordinal);
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
            _log.Add($"{DateTime.Now:HH:mm:ss} {Redact(message)}");
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
