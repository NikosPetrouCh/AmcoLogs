using AmcoLogs.Core.Excel;
using AmcoLogs.Core.Persistence;
using AmcoLogs.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace AmcoLogs.Web.Controllers;

[ApiController]
[Route("api/imports")]
public sealed class ImportsController : ControllerBase
{
    private readonly IExcelImportParser _parser;
    private readonly IAmcoLogsStore _store;
    private readonly MySqlDeskArchive _desk;

    public ImportsController(IExcelImportParser parser, IAmcoLogsStore store, MySqlDeskArchive desk)
    {
        _parser = parser;
        _store = store;
        _desk = desk;
    }

    [HttpPost("excel")]
    [RequestSizeLimit(52_428_800)]
    public async Task<ActionResult<ExcelImportResponse>> Upload(
        IFormFile? file,
        [FromQuery] bool persist = true,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Δεν επιλέχθηκε αρχείο Excel." });
        }

        var extension = Path.GetExtension(file.FileName);
        if (!extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Υποστηρίζονται αρχεία .xlsx." });
        }

        await using var stream = file.OpenReadStream();
        var parsed = _parser.Parse(stream, file.FileName);
        var screened = parsed.Profile == ExcelImportResult.OasaProfile
            ? parsed
            : TransactionScreen.Apply(parsed);

        SavedExcelImport? saved = null;
        string? persistMessage = null;
        if (persist)
        {
            var status = await _store.GetStatusAsync(cancellationToken);
            if (!status.Connected)
            {
                persistMessage = "Το Excel διαβάστηκε, αλλά δεν αποθηκεύτηκε γιατί δεν υπάρχει σύνδεση MySQL.";
            }
            else
            {
                saved = await _store.SaveImportAsync(TransactionScreen.ForStorage(screened), cancellationToken);
                if (saved is not null && screened.Profile == ExcelImportResult.OsethProfile)
                {
                    try
                    {
                        await _desk.SaveExcelCaseAsync("OSETH", null, screened, cancellationToken);
                        persistMessage = "Γράφτηκε και στο General OSETH.";
                    }
                    catch (InvalidOperationException exception)
                    {
                        persistMessage = exception.Message;
                    }
                }
            }
        }

        return Ok(new ExcelImportResponse
        {
            Import = screened,
            Saved = saved,
            PersistMessage = persistMessage,
            NeedsNetwork = screened.Profile == ExcelImportResult.OasaProfile
        });
    }

    [HttpGet("rows")]
    public async Task<ActionResult<IReadOnlyList<StoredExcelRow>>> Rows(CancellationToken cancellationToken)
    {
        return Ok(await _store.ListAllExcelRowsAsync(2000, cancellationToken));
    }

    [HttpGet("{id:guid}/rows")]
    public async Task<ActionResult<IReadOnlyList<StoredExcelRow>>> BatchRows(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await _store.ListBatchRowsAsync(id, cancellationToken));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavedExcelImport>>> Recent(CancellationToken cancellationToken)
    {
        return Ok(await _store.ListRecentImportsAsync(200, cancellationToken));
    }
}

public sealed class ExcelImportResponse
{
    public required ExcelImportResult Import { get; init; }
    public SavedExcelImport? Saved { get; init; }
    public string? PersistMessage { get; init; }
    public bool NeedsNetwork { get; init; }
}
