namespace AmcoLogs.Core.Logs;

public sealed class LogDownloadDeviceRequest
{
    public required string ServiceAccountId { get; init; }
    public required string Mobility { get; init; }
    public required string IpAddress { get; init; }
    public List<string> LogDates { get; init; } = [];
}

public sealed class LogDownloadResult
{
    public required string ServiceAccountId { get; init; }
    public string? IpAddress { get; init; }
    public required string Mobility { get; init; }
    public bool Reachable { get; init; }
    public required string Status { get; init; }
    public string? LocalPath { get; init; }
    public string? Message { get; init; }
}

public sealed class LogDownloadJobStatus
{
    public bool Running { get; init; }
    public string Phase { get; init; } = "";
    public IReadOnlyList<string> Log { get; init; } = [];
    public IReadOnlyList<LogDownloadResult> Results { get; init; } = [];
}

public interface ILogDownloadJob
{
    LogDownloadJobStatus GetStatus();
    Task<bool> StartAsync(IReadOnlyList<LogDownloadDeviceRequest> devices, CancellationToken cancellationToken = default);
}
