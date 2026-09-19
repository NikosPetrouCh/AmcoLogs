namespace AmcoLogs.Core.Persistence;

public sealed class DeviceIpMapping
{
    public required string ServiceAccountId { get; init; }
    public string? BusNo { get; init; }
    public required string IpAddress { get; init; }
    public required string Mobility { get; init; }
    public required string Source { get; init; }
    public DateTime UpdatedAt { get; init; }
}
