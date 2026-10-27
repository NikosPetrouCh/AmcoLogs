using System.Diagnostics;
using System.Globalization;
using System.Text;
using AmcoLogs.Core.Logs;
using Renci.SshNet;

namespace AmcoLogs.Infrastructure.Logs;

public sealed class RemoteLogDownloader : ILogDownloadJob
{
    private const string OpenSshDirectory = @"C:\Windows\System32\OpenSSH";
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
            var root = ResolveLocalFolder();
            var fixedCount = devices.Count(device => !IsMobile(device.Mobility));
            var mobileCount = devices.Count - fixedCount;
            Log($"Κατέβασμα {devices.Count} συσκευών με επιτυχές ping: {fixedCount} σταθερές, {mobileCount} κινητές.");
            Log(string.IsNullOrEmpty(_settings.Password)
                ? "Δεν έχει φορτωθεί κωδικός συσκευής. Μπαίνει στο appsettings.Local.json και μετά θέλει επανεκκίνηση της εφαρμογής."
                : "Κωδικός συσκευής φορτωμένος.");
            Log($"Τα αρχεία μπαίνουν στο {root}\\<κωδικός συσκευής>.");
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
            Log("Τέλος κατεβάσματος.");
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

        if (string.IsNullOrEmpty(_settings.Password))
        {
            return Fail(device, "error", "Δεν έχει φορτωθεί κωδικός συσκευής. Βάλε τον στο appsettings.Local.json και κάνε restart.");
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
                DownloadFile(device.IpAddress, port, user, remoteFile, destDir, remoteName, 180_000);
                var saved = new FileInfo(localFile);
                if (!saved.Exists || saved.Length == 0)
                {
                    throw new InvalidOperationException($"Το {remoteName} δεν εμφανίστηκε στο {destDir}.");
                }

                Log($"Αποθηκεύτηκε {saved.FullName} ({saved.Length} bytes).");
                expected.Add(saved.FullName);
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

            var message = $"Κατέβηκαν {expected.Count} αρχεία στο {destDir}.";
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
        Log($"ssh -p {port} {user}@{ip}");
        Log($"cd {remoteDir}");
        Log($"sudo {action}");
        var remote = $"cd {QuoteShell(remoteDir)} && sudo -S -p '' {action}";
        var ssh = Path.Combine(OpenSshDirectory, "ssh.exe");
        if (File.Exists(ssh))
        {
            try
            {
                RunOpenSsh(ssh, SshOptions(port, $"{user}@{ip}", remote), timeoutMs, _settings.Password);
                return;
            }
            catch (Exception exception)
            {
                Log($"ssh.exe: {Redact(exception.Message)}");
            }
        }

        RunSudoWithLibrary(ip, port, user, remoteDir, action, timeoutMs);
    }

    private void RunSudoWithLibrary(string ip, int port, string user, string remoteDir, string action, int timeoutMs)
    {
        var password = _settings.Password ?? "";
        var commandText = $"cd {QuoteShell(remoteDir)} && echo {QuoteShell(password)} | sudo -S -p '' {action}";
        using var client = Open<SshClient>(ip, port, user);
        client.Connect();
        try
        {
            using var command = client.CreateCommand(commandText);
            command.CommandTimeout = TimeSpan.FromMilliseconds(timeoutMs);
            var output = command.Execute();
            var text = Redact((output + "\n" + command.Error).Trim());
            if (!string.IsNullOrWhiteSpace(text))
            {
                Log(text);
            }

            if (command.ExitStatus != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(text)
                    ? $"Η εντολή στη συσκευή απέτυχε (exit {command.ExitStatus})."
                    : text);
            }
        }
        finally
        {
            client.Disconnect();
        }
    }

    private void DownloadFile(string ip, int port, string user, string remoteFile, string destDir, string remoteName, int timeoutMs)
    {
        var localFile = Path.Combine(destDir, remoteName);
        TryDelete(localFile);
        var scp = Path.Combine(OpenSshDirectory, "scp.exe");
        if (File.Exists(scp))
        {
            try
            {
                RunOpenSsh(
                    scp,
                    SshOptions(port, $"{user}@{ip}:{remoteFile}", destDir, capitalPort: true),
                    timeoutMs,
                    null);
                var saved = new FileInfo(localFile);
                if (saved.Exists && saved.Length > 0)
                {
                    return;
                }

                throw new InvalidOperationException($"Το scp.exe δεν έγραψε το {remoteName} στο {destDir}.");
            }
            catch (Exception exception)
            {
                TryDelete(localFile);
                Log($"scp.exe: {Redact(exception.Message)}");
            }
        }

        DownloadWithSftp(ip, port, user, remoteFile, localFile, timeoutMs);
        var created = new FileInfo(localFile);
        if (!created.Exists || created.Length == 0)
        {
            TryDelete(localFile);
            throw new InvalidOperationException($"Το {remoteName} δεν εμφανίστηκε στο {destDir}.");
        }
    }

    private void DownloadWithSftp(string ip, int port, string user, string remoteFile, string localFile, int timeoutMs)
    {
        using var client = Open<SftpClient>(ip, port, user);
        client.OperationTimeout = TimeSpan.FromMilliseconds(timeoutMs);
        client.Connect();
        try
        {
            using var stream = File.Create(localFile);
            client.DownloadFile(remoteFile, stream);
        }
        finally
        {
            client.Disconnect();
        }
    }

    private List<string> SshOptions(int port, string target, string trailing, bool capitalPort = false)
    {
        var hosts = KnownHostsPath();
        return
        [
            capitalPort ? "-P" : "-p",
            port.ToString(CultureInfo.InvariantCulture),
            "-o", "StrictHostKeyChecking=accept-new",
            "-o", "UserKnownHostsFile=" + hosts,
            "-o", "ConnectTimeout=20",
            "-o", "NumberOfPasswordPrompts=1",
            "-o", "PubkeyAuthentication=no",
            "-o", "PreferredAuthentications=password,keyboard-interactive",
            target,
            trailing
        ];
    }

    private void RunOpenSsh(string executable, IReadOnlyList<string> arguments, int timeoutMs, string? stdinLine)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["SSH_ASKPASS"] = AskpassPath();
        start.Environment["SSH_ASKPASS_REQUIRE"] = "force";
        start.Environment["DISPLAY"] = "localhost:0";
        start.Environment["AMCO_DEVICE_PASSWORD"] = _settings.Password ?? "";

        using var process = new Process { StartInfo = start };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                stdout.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                stderr.AppendLine(args.Data);
            }
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (stdinLine != null)
        {
            process.StandardInput.WriteLine(stdinLine);
        }

        process.StandardInput.Close();
        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }

            throw new TimeoutException($"Ξεπεράστηκε το όριο των {timeoutMs / 1000} δευτερολέπτων.");
        }

        process.WaitForExit();
        var text = Redact((stdout.ToString() + stderr).Trim());
        if (!string.IsNullOrWhiteSpace(text))
        {
            Log(text);
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(text)
                ? $"{Path.GetFileName(executable)} απέτυχε (exit {process.ExitCode})."
                : text);
        }
    }

    private static string AskpassPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AmcoLogs");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "askpass.exe");
        if (File.Exists(exe))
        {
            return exe;
        }

        var source = Path.Combine(dir, "askpass.cs");
        File.WriteAllText(source, """
            using System;
            static class Askpass
            {
                static void Main()
                {
                    Console.WriteLine(Environment.GetEnvironmentVariable("AMCO_DEVICE_PASSWORD") ?? "");
                }
            }
            """);
        var csc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        var compile = new ProcessStartInfo(csc)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        compile.ArgumentList.Add("/nologo");
        compile.ArgumentList.Add("/t:exe");
        compile.ArgumentList.Add("/out:" + exe);
        compile.ArgumentList.Add(source);
        using var process = Process.Start(compile) ?? throw new InvalidOperationException("Δεν ξεκίνησε ο compiler για τον κωδικό ssh.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);
        if (!File.Exists(exe))
        {
            throw new InvalidOperationException("Δεν φτιάχτηκε το βοηθητικό για τον κωδικό ssh. " + error.Trim());
        }

        return exe;
    }

    private static string KnownHostsPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AmcoLogs");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "known_hosts");
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

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

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
