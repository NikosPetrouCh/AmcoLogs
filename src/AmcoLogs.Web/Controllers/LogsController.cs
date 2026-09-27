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
    private readonly INlogSaleLocator _locator;
    private readonly SaleInvestigation _investigation;
    private readonly IAmcoLogsStore _store;

    public LogsController(
        ILogDownloadJob job,
        INlogSaleLocator locator,
        SaleInvestigation investigation,
        IAmcoLogsStore store)
    {
        _job = job;
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
            .Where(device => !string.IsNullOrWhiteSpace(device.IpAddress))
            .ToList();
        if (devices.Count == 0)
        {
            return BadRequest(new { message = "No devices with an IP address." });
        }

        var started = await _job.StartAsync(devices, cancellationToken);
        if (!started)
        {
            return Conflict(new { message = "A log download is already running." });
        }

        return Accepted(_job.GetStatus());
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

public sealed class ProcessLogsRequest
{
    public List<InvestigationSubject>? Transactions { get; set; }
    public List<LogFileRef>? Logs { get; set; }
}
