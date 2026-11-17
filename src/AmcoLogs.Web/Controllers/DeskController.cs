using AmcoLogs.Core.Persistence;
using AmcoLogs.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace AmcoLogs.Web.Controllers;

[ApiController]
[Route("api/desk")]
public sealed class DeskController : ControllerBase
{
    private readonly MySqlDeskArchive _archive;

    public DeskController(MySqlDeskArchive archive)
    {
        _archive = archive;
    }

    [HttpGet("cases")]
    public Task<IActionResult> Cases([FromQuery] string? @operator, [FromQuery] string? network, CancellationToken cancellationToken) =>
        Run(() => _archive.ListCasesAsync(@operator, network, cancellationToken));

    [HttpGet("lines")]
    public Task<IActionResult> Lines([FromQuery] string? @operator, [FromQuery] string? network, [FromQuery] Guid? caseId, CancellationToken cancellationToken) =>
        Run(() => _archive.ListLinesAsync(@operator, network, caseId, cancellationToken));

    [HttpPost("cases")]
    public Task<IActionResult> SaveCase([FromBody] DeskCaseDraft draft, CancellationToken cancellationToken) =>
        Run(() => _archive.SaveCaseAsync(draft, cancellationToken));

    [HttpPatch("cases/{id:guid}")]
    public Task<IActionResult> PatchCase(Guid id, [FromBody] DeskCasePatch patch, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            await _archive.PatchCaseAsync(id, patch, cancellationToken);
            return new { ok = true };
        });

    [HttpPatch("lines/{id:guid}")]
    public Task<IActionResult> PatchLine(Guid id, [FromBody] DeskLinePatch patch, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            await _archive.PatchLineAsync(id, patch, cancellationToken);
            return new { ok = true };
        });

    [HttpGet("ips")]
    public Task<IActionResult> Ips([FromQuery] string? @operator, [FromQuery] string? network, CancellationToken cancellationToken) =>
        Run(() => _archive.ListIpsAsync(@operator, network, cancellationToken));

    [HttpPost("ips")]
    public Task<IActionResult> SaveIp([FromBody] DeskIpRow row, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(row.ServiceAccountId) || string.IsNullOrWhiteSpace(row.IpAddress))
            {
                throw new InvalidOperationException("Χρειάζονται CTVM και IP.");
            }

            await _archive.UpsertIpAsync(row, cancellationToken);
            return new { ok = true };
        });

    [HttpGet("errors")]
    public Task<IActionResult> Errors([FromQuery] string? @operator, [FromQuery] string? network, CancellationToken cancellationToken) =>
        Run(() => _archive.ListErrorsAsync(@operator, network, cancellationToken));

    [HttpPost("errors")]
    public Task<IActionResult> SaveError([FromBody] DeskErrorEntry entry, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(entry.ServiceAccountId))
            {
                throw new InvalidOperationException("Χρειάζεται CTVM.");
            }

            return await _archive.AddErrorAsync(entry, cancellationToken);
        });

    private async Task<IActionResult> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
