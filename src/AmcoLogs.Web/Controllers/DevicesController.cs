using AmcoLogs.Core.Device;
using AmcoLogs.Core.Persistence;
using AmcoLogs.Infrastructure.Device;
using Microsoft.AspNetCore.Mvc;

namespace AmcoLogs.Web.Controllers;

[ApiController]
[Route("api/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly IAmcoLogsStore _store;
    private readonly IIpCatalog _catalog;
    private readonly ManualIpCache _manual;

    public DevicesController(IAmcoLogsStore store, IIpCatalog catalog, ManualIpCache manual)
    {
        _store = store;
        _catalog = catalog;
        _manual = manual;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DeviceIpMapping>>> List(CancellationToken cancellationToken)
    {
        var stored = await _store.ListDeviceIpsAsync(cancellationToken);
        return Ok(IpDirectory.Union(_catalog.All(), stored.Concat(_manual.All())));
    }

    [HttpPost("resolve")]
    public async Task<ActionResult<IpResolveResult>> Resolve(
        [FromBody] DeviceResolveRequest request,
        CancellationToken cancellationToken)
    {
        var ids = (request.ServiceAccountIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var stored = (await _store.ListDeviceIpsAsync(cancellationToken)).Concat(_manual.All()).ToList();
        var hits = new List<DeviceIpHit>();
        var missing = new List<string>();

        foreach (var id in ids)
        {
            var combined = IpDirectory.Combine(id, _catalog.Find(id), IpDirectory.FindStored(stored, id));
            if (combined is null)
            {
                missing.Add(id);
                continue;
            }

            hits.Add(combined);
            await TryPersistAsync(ToMapping(combined), cancellationToken);
        }

        return Ok(new IpResolveResult
        {
            CatalogCount = _catalog.Count,
            MatchedCount = hits.Count,
            Hits = hits,
            Missing = missing
        });
    }

    [HttpPost("ip")]
    public async Task<IActionResult> UpsertIp([FromBody] DeviceIpUpdate update, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(update.ServiceAccountId) || string.IsNullOrWhiteSpace(update.IpAddress))
        {
            return BadRequest(new { message = "CTVM / ServiceAccountID and IP are required." });
        }

        if (!System.Net.IPAddress.TryParse(update.IpAddress.Trim(), out _))
        {
            return BadRequest(new { message = "Invalid IP address." });
        }

        var id = update.ServiceAccountId.Trim();
        var mobility = string.IsNullOrWhiteSpace(update.Mobility) || update.Mobility == "Unknown"
            ? DeviceCode.Classify(id).ToString()
            : update.Mobility.Trim();
        var mapping = new DeviceIpMapping
        {
            ServiceAccountId = id,
            IpAddress = update.IpAddress.Trim(),
            BusNo = string.IsNullOrWhiteSpace(update.BusNo) ? null : update.BusNo.Trim(),
            Mobility = mobility,
            Source = "manual",
            UpdatedAt = DateTime.UtcNow
        };

        _manual.Upsert(mapping);
        var persisted = await TryPersistAsync(mapping, cancellationToken);
        return Ok(new
        {
            ok = true,
            persisted,
            mapping,
            message = persisted
                ? null
                : "Kept for this session. Connect MySQL under Database to store it."
        });
    }

    private async Task<bool> TryPersistAsync(DeviceIpMapping mapping, CancellationToken cancellationToken)
    {
        try
        {
            await _store.UpsertDeviceIpAsync(mapping, cancellationToken);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static DeviceIpMapping ToMapping(DeviceIpHit hit) => new()
    {
        ServiceAccountId = hit.ServiceAccountId,
        IpAddress = hit.IpAddress,
        BusNo = hit.BusNo,
        Mobility = hit.Mobility,
        Source = hit.Source,
        UpdatedAt = DateTime.UtcNow
    };
}

public sealed class DeviceIpUpdate
{
    public string ServiceAccountId { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string? BusNo { get; set; }
    public string Mobility { get; set; } = "Unknown";
}

public sealed class DeviceResolveRequest
{
    public List<string>? ServiceAccountIds { get; set; }
}
