namespace AmcoLogs.Core.Device;

public sealed class InvestigationDevice
{
    public Guid Id { get; set; }

    public Guid BatchId { get; set; }

    public string ServiceAccountId { get; set; } = "";

    public string? CtvmNumber { get; set; }

    public string? IpAddress { get; set; }

    public string? StationName { get; set; }

    public string Mobility { get; set; } = "";

    public string PingStatus { get; set; } = "pending";

    public string LogsStatus { get; set; } = "pending";

    public string FindingStatus { get; set; } = "pending";

    public string CurrentStep { get; set; } = "ping";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
