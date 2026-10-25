using AmcoLogs.Core.Excel;
using AmcoLogs.Core.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace AmcoLogs.Web.Controllers;

[ApiController]
[Route("api/imports")]
public sealed class ImportsController : ControllerBase
{
    private readonly IExcelImportParser _parser;
    private readonly IAmcoLogsStore _store;

    public ImportsController(IExcelImportParser parser, IAmcoLogsStore store)
    {
        _parser = parser;
        _store = store;
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
        var screened = TransactionScreen.Apply(_parser.Parse(stream, file.FileName));

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
            }
        }

        return Ok(new ExcelImportResponse
        {
            Import = screened,
            Saved = saved,
            PersistMessage = persistMessage
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
}
