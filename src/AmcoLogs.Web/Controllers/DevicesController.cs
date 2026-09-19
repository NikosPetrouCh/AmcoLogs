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
}
