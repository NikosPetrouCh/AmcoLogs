namespace AmcoLogs.Core.Device;

public sealed record DeviceIpHit
{
    public required string ServiceAccountId { get; init; }
    public required string IpAddress { get; init; }
    public string? BusNo { get; init; }
    public required string Mobility { get; init; }
    public required string Source { get; init; }
}

public sealed class IpResolveResult
{
    public int CatalogCount { get; init; }
    public int MatchedCount { get; init; }
    public IReadOnlyList<DeviceIpHit> Hits { get; init; } = [];
    public IReadOnlyList<string> Missing { get; init; } = [];
}

public interface IIpCatalog
{
    int Count { get; }
    DeviceIpHit? Find(string? serviceAccountId, string? mobility = null);
    IpResolveResult Resolve(IEnumerable<string> serviceAccountIds);
    IReadOnlyList<DeviceIpHit> All();
}
