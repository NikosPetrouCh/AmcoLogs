namespace AmcoLogs.Core.ThingsBoard;

public sealed class ThingsBoardDeviceRequest
{
    public required string ServiceAccountId { get; init; }
    public required string Mobility { get; init; }
}

public sealed class ThingsBoardLookupResult
{
    public required string ServiceAccountId { get; init; }
    public string? BusNo { get; init; }
    public string? IpAddress { get; init; }
    public required string Status { get; init; }
    public string? Message { get; init; }
}

public sealed class ThingsBoardJobStatus
{
    public bool Running { get; init; }
    public string Phase { get; init; } = "";
    public IReadOnlyList<string> Log { get; init; } = [];
    public IReadOnlyList<ThingsBoardLookupResult> Results { get; init; } = [];
}

public interface IThingsBoardLookup
{
    ThingsBoardJobStatus GetStatus();
    Task<bool> StartAsync(IReadOnlyList<ThingsBoardDeviceRequest> devices, CancellationToken cancellationToken = default);
}
