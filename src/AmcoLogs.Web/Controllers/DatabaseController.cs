using AmcoLogs.Core.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace AmcoLogs.Web.Controllers;

[ApiController]
[Route("api/database")]
public sealed class DatabaseController : ControllerBase
{
    private readonly IAmcoLogsStore _store;

    public DatabaseController(IAmcoLogsStore store)
    {
        _store = store;
    }

    [HttpGet("status")]
    public async Task<ActionResult<DatabaseStatus>> Status(CancellationToken cancellationToken)
    {
        return Ok(await _store.GetStatusAsync(cancellationToken));
    }

    [HttpPost("connect")]
    public async Task<ActionResult<DatabaseStatus>> Connect(
        [FromBody] MySqlConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.User))
        {
            return BadRequest(new { message = "Συμπλήρωσε host και χρήστη MySQL." });
        }

        if (!IsSafeDatabaseName(settings.Database))
        {
            return BadRequest(new { message = "Το όνομα βάσης μπορεί να έχει μόνο γράμματα, αριθμούς και _." });
        }

        try
        {
            return Ok(await _store.ConnectAsync(settings, cancellationToken));
        }
        catch (Exception exception)
        {
            return BadRequest(new DatabaseStatus
            {
                Connected = false,
                Host = settings.Host,
                User = settings.User,
                Database = settings.Database,
                Message = exception.Message
            });
        }
    }

    private static bool IsSafeDatabaseName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.All(character => char.IsLetterOrDigit(character) || character == '_');
}
