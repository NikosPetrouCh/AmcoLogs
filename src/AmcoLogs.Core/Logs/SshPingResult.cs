namespace AmcoLogs.Core.Logs;

public sealed class SshPingTarget
{
    public required string ServiceAccountId { get; init; }
    public required string IpAddress { get; init; }
}

public sealed class SshPingResult
{
    public required string ServiceAccountId { get; init; }
    public required string IpAddress { get; init; }
    public bool Reachable { get; init; }
    public string? Path { get; init; }
    public int? Port { get; init; }
    public required string Message { get; init; }
}
