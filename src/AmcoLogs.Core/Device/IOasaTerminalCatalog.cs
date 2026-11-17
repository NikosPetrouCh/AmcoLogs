namespace AmcoLogs.Core.Device;

public sealed record OasaTerminalHit
{
    public required string Terminal { get; init; }
    public required string ServiceAccountId { get; init; }
    public required string IpAddress { get; init; }
    public string? Station { get; init; }
}

public interface IOasaTerminalCatalog
{
    int Count { get; }
    OasaTerminalHit? Find(string? terminal);
}
