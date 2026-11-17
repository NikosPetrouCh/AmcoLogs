using AmcoLogs.Core.Logs;
using AmcoLogs.Core.Persistence;
using AmcoLogs.Infrastructure.Logs;
using Microsoft.AspNetCore.Mvc;

namespace AmcoLogs.Web.Controllers;

[ApiController]
[Route("api/logs")]
public sealed class LogsController : ControllerBase
{
    private readonly ILogDownloadJob _job;
    private readonly SshConnectionProbe _ping;
    private readonly NlogSliceReader _slices;
    private readonly ITransactionFlowReader _flow;
    private readonly DeviceAccessSettings _access;
    private readonly INlogSaleLocator _locator;
    private readonly SaleInvestigation _investigation;
    private readonly IAmcoLogsStore _store;

    public LogsController(
        ILogDownloadJob job,
        SshConnectionProbe ping,
        NlogSliceReader slices,
        ITransactionFlowReader flow,
        DeviceAccessSettings access,
        INlogSaleLocator locator,
        SaleInvestigation investigation,
        IAmcoLogsStore store)
    {
        _job = job;
        _ping = ping;
        _slices = slices;
        _flow = flow;
        _access = access;
        _locator = locator;
        _investigation = investigation;
        _store = store;
    }

    [HttpGet("status")]
    public ActionResult<LogDownloadJobStatus> Status() => Ok(_job.GetStatus());

    [HttpGet("findings")]
    public async Task<ActionResult<IReadOnlyList<InvestigationFinding>>> Findings(CancellationToken cancellationToken)
    {
        return Ok(await _store.ListFindingsAsync(500, cancellationToken));
    }

    [HttpPost("download")]
    public async Task<IActionResult> Download(
        [FromBody] LogDownloadRequest request,
        CancellationToken cancellationToken)
    {
        var devices = (request.Devices ?? [])
            .Where(device => device.PingReachable && !string.IsNullOrWhiteSpace(device.IpAddress))
            .ToList();
        if (devices.Count == 0)
        {
            return BadRequest(new { message = "Κατέβασμα μόνο για συσκευές που απάντησαν στο ping." });
        }

        var started = await _job.StartAsync(devices, cancellationToken);
        if (!started)
        {
            return Conflict(new { message = "A log download is already running." });
        }

        return Accepted(_job.GetStatus());
    }

    [HttpPost("ping")]
    public async Task<ActionResult<IReadOnlyList<SshPingResult>>> Ping(
        [FromBody] SshPingRequest request,
        CancellationToken cancellationToken)
    {
        var targets = (request.Devices ?? [])
            .Where(device => !string.IsNullOrWhiteSpace(device.ServiceAccountId) && !string.IsNullOrWhiteSpace(device.IpAddress))
            .Select(device => new SshPingTarget
            {
                ServiceAccountId = device.ServiceAccountId.Trim(),
                IpAddress = device.IpAddress.Trim()
            })
            .ToList();
        if (targets.Count == 0)
        {
            return BadRequest(new { message = "Δεν υπάρχει συσκευή με IP για ping." });
        }

        return Ok(await _ping.PingAsync(targets, cancellationToken));
    }

    [HttpPost("upload")]
    [RequestSizeLimit(314_572_800)]
    [RequestFormLimits(MultipartBodyLengthLimit = 314_572_800)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile? file,
        [FromForm] string? serviceAccountId,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "No nlog file." });
        }

        if (string.IsNullOrWhiteSpace(serviceAccountId))
        {
            return BadRequest(new { message = "ServiceAccountID is required." });
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AmcoLogs",
            "uploads",
            Sanitize(serviceAccountId.Trim()));
        Directory.CreateDirectory(root);
        var name = Sanitize(file.FileName);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "nlog-upload.log";
        }

        var path = Path.Combine(root, name);
        await using (var stream = System.IO.File.Create(path))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        return Ok(new
        {
            serviceAccountId = serviceAccountId.Trim(),
            localPath = path,
            fileName = name
        });
    }

    [HttpPost("slice")]
    public ActionResult<LogSliceResult> Slice([FromBody] LogSliceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return BadRequest(new { message = "Χρειάζεται το log." });
        }

        if (!IsAllowedLogPath(request.Path))
        {
            return BadRequest(new { message = "Το log δεν είναι σε επιτρεπόμενο φάκελο." });
        }

        if (request.WholeFile)
        {
            return Ok(_slices.ReadWhole(request.Path));
        }

        var clockText = request.ClockTime;
        if (string.IsNullOrWhiteSpace(clockText)
            && string.Equals(request.Profile, "OSETH", StringComparison.OrdinalIgnoreCase))
        {
            clockText = TransactionClock.FromOsethTransactionId(request.TransactionId);
        }

        if (string.IsNullOrWhiteSpace(clockText)
            && string.Equals(request.Profile, "OASA", StringComparison.OrdinalIgnoreCase))
        {
            clockText = TransactionClock.FromTimestamp(request.TransactionDate);
        }

        if (TransactionClock.TryParse(clockText, out var clock))
        {
            return Ok(WithFlow(_slices.ReadByClock(request.Path, clock, request.TransactionDate), request.Profile, request.Path, clockText));
        }

        if (string.IsNullOrWhiteSpace(request.TransactionId))
        {
            return BadRequest(new { message = "Χρειάζεται ώρα συναλλαγής ή Transaction ID." });
        }

        return Ok(WithFlow(_slices.Read(request.Path, request.TransactionId, request.TransactionDate), request.Profile, request.Path, clockText));
    }

    [HttpPost("inspect")]
    [RequestSizeLimit(314_572_800)]
    [RequestFormLimits(MultipartBodyLengthLimit = 314_572_800)]
    [Consumes("multipart/form-data")]
    public ActionResult<LogInspectResult> Inspect(
        [FromForm] IFormFile? file,
        [FromForm] string? transactionIds)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "No nlog file." });
        }

        var ids = ReadIds(transactionIds);
        if (ids.Count == 0)
        {
            return BadRequest(new { message = "Inspect only the Excel Transaction ID. Drop Excel first." });
        }

        using var stream = OpenUploaded(file);
        return Ok(_locator.Inspect(stream, ids));
    }

    [HttpPost("process")]
    public async Task<ActionResult<InvestigationRunResult>> Process(
        [FromBody] ProcessLogsRequest request,
        CancellationToken cancellationToken)
    {
        var subjects = request.Transactions ?? [];
        var logs = (request.Logs ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.ServiceAccountId) && !string.IsNullOrWhiteSpace(item.Path))
            .ToList();

        if (subjects.Count == 0)
        {
            return BadRequest(new { message = "No Excel transactions to investigate." });
        }

        if (subjects.All(item => string.IsNullOrWhiteSpace(item.TransactionId)))
        {
            return BadRequest(new { message = "The Excel has no Transaction ID values." });
        }

        var findings = _investigation.Run(subjects, logs);
        var persisted = false;
        string? persistMessage = null;
        var status = await _store.GetStatusAsync(cancellationToken);
        if (!status.Connected)
        {
            persistMessage = "Το πόρισμα βγήκε, αλλά δεν αποθηκεύτηκε γιατί δεν υπάρχει σύνδεση MySQL.";
        }
        else
        {
            await _store.SaveFindingsAsync(findings, cancellationToken);
            persisted = true;
        }

        return Ok(new InvestigationRunResult
        {
            SubjectCount = subjects.Count,
            LogFileCount = logs.Sum(item => NlogFile.Enumerate(item.Path).Count),
            LocatedCount = findings.Count(item =>
                item.Verdict is InvestigationVerdict.LocatedNoErrors or InvestigationVerdict.LocatedWithErrors),
            ErrorSaleCount = findings.Count(item => item.Verdict == InvestigationVerdict.LocatedWithErrors),
            NotFoundCount = findings.Count(item => item.Verdict == InvestigationVerdict.NotFoundInLog),
            MissingLogCount = findings.Count(item => item.Verdict == InvestigationVerdict.NoLogFile),
            Persisted = persisted,
            PersistMessage = persistMessage,
            Findings = findings
        });
    }

    private static List<string> ReadIds(string? transactionIds)
    {
        if (string.IsNullOrWhiteSpace(transactionIds))
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(transactionIds) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static Stream OpenUploaded(IFormFile file)
    {
        if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return file.OpenReadStream();
        }

        var temp = Path.Combine(Path.GetTempPath(), "amco-upload-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var stream = System.IO.File.Create(temp))
        {
            file.CopyTo(stream);
        }

        return NlogFile.OpenRead(temp);
    }

    private LogSliceResult WithFlow(LogSliceResult slice, string? profile, string sourcePath, string? clockText)
    {
        if (!slice.Found)
        {
            return slice;
        }

        return new LogSliceResult
        {
            Found = slice.Found,
            Message = slice.Message,
            LogFileName = slice.LogFileName,
            LineStart = slice.LineStart,
            LineEnd = slice.LineEnd,
            Lines = slice.Lines,
            Flow = _flow.Read(slice.Lines.Select(line => line.Text).ToArray(), profile),
            SavedPath = SaveOpenedSlice(sourcePath, slice, clockText)
        };
    }

    private static string? SaveOpenedSlice(string sourcePath, LogSliceResult slice, string? clockText)
    {
        if (slice.Lines.Count == 0)
        {
            return null;
        }

        var fileName = Path.GetFileName(sourcePath);
        if (fileName.StartsWith("nlog-slice-", StringComparison.OrdinalIgnoreCase))
        {
            return sourcePath;
        }

        var dir = Directory.Exists(sourcePath) ? sourcePath : Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return null;
        }

        var first = slice.Lines[0].Text ?? "";
        var day = first.Length >= 10
            && DateOnly.TryParseExact(first[..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)
            ? first[..10]
            : "day";
        var hhmm = TransactionClock.TryParse(clockText, out var clock)
            ? clock.ToString("HHmm", System.Globalization.CultureInfo.InvariantCulture)
            : "time";
        var path = Path.Combine(dir, $"nlog-slice-{day}-{hhmm}.log");
        System.IO.File.WriteAllLines(path, slice.Lines.Select(line => line.Text));
        return path;
    }

    private bool IsAllowedLogPath(string path)
    {
        var full = Path.GetFullPath(path);
        foreach (var root in AllowedLogRoots())
        {
            var folder = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Equals(folder, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<string> AllowedLogRoots()
    {
        if (!string.IsNullOrWhiteSpace(_access.LocalDownloadFolder))
        {
            yield return _access.LocalDownloadFolder;
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Amco files");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AmcoLogs",
            "uploads");
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}

public sealed class LogDownloadRequest
{
    public List<LogDownloadDeviceRequest>? Devices { get; set; }
}

public sealed class SshPingRequest
{
    public List<SshPingTarget>? Devices { get; set; }
}

public sealed class LogSliceRequest
{
    public string? Path { get; set; }
    public string? TransactionId { get; set; }
    public DateTime? TransactionDate { get; set; }
    public string? ClockTime { get; set; }
    public string? Profile { get; set; }
    public bool WholeFile { get; set; }
}

public sealed class ProcessLogsRequest
{
    public List<InvestigationSubject>? Transactions { get; set; }
    public List<LogFileRef>? Logs { get; set; }
}
