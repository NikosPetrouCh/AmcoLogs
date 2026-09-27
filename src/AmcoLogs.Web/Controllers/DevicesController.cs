using AmcoLogs.Core.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace AmcoLogs.Web.Controllers;

[ApiController]
[Route("api/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly IAmcoLogsStore _store;

    public DevicesController(IAmcoLogsStore store)
    {
        _store = store;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeviceIpMapping>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _store.ListDeviceIpsAsync(cancellationToken));
    }

    [HttpPost("ip")]
    public async Task<IActionResult> UpsertIp([FromBody] DeviceIpUpdate update, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(update.ServiceAccountId) || string.IsNullOrWhiteSpace(update.IpAddress))
        {
            return BadRequest(new { message = "ServiceAccountID and IP are required." });
        }

        if (!System.Net.IPAddress.TryParse(update.IpAddress.Trim(), out _))
        {
            return BadRequest(new { message = "Invalid IP address." });
        }

        var mapping = new DeviceIpMapping
        {
            ServiceAccountId = update.ServiceAccountId.Trim(),
            IpAddress = update.IpAddress.Trim(),
            Mobility = string.IsNullOrWhiteSpace(update.Mobility) ? "Unknown" : update.Mobility,
            Source = "manual",
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            await _store.UpsertDeviceIpAsync(mapping, cancellationToken);
            return Ok(new { ok = true, persisted = true });
        }
        catch (InvalidOperationException)
        {
            return Ok(new
            {
                ok = true,
                persisted = false,
                message = "IP kept for this session. Connect MySQL under Database to store it."
            });
        }
    }
}

public sealed class DeviceIpUpdate
{
    public string ServiceAccountId { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string Mobility { get; set; } = "Unknown";
}
