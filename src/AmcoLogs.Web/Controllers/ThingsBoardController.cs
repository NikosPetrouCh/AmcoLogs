using AmcoLogs.Core.ThingsBoard;
using Microsoft.AspNetCore.Mvc;

namespace AmcoLogs.Web.Controllers;

[ApiController]
[Route("api/thingsboard")]
public sealed class ThingsBoardController : ControllerBase
{
    private readonly IThingsBoardLookup _lookup;

    public ThingsBoardController(IThingsBoardLookup lookup)
    {
        _lookup = lookup;
    }

    [HttpGet("status")]
    public ActionResult<ThingsBoardJobStatus> Status() => Ok(_lookup.GetStatus());

    [HttpPost("lookup")]
    public async Task<IActionResult> Lookup(
        [FromBody] ThingsBoardLookupRequest request,
        CancellationToken cancellationToken)
    {
        var devices = request.Devices ?? [];
        if (devices.Count == 0)
        {
            return BadRequest(new { message = "No devices to look up." });
        }

        var started = await _lookup.StartAsync(devices, cancellationToken);
        if (!started)
        {
            return Conflict(new { message = "A ThingsBoard lookup is already running." });
        }

        return Accepted(_lookup.GetStatus());
    }
}

public sealed class ThingsBoardLookupRequest
{
    public List<ThingsBoardDeviceRequest>? Devices { get; set; }
}
