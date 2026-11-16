namespace AmcoLogs.Core.Persistence;

public sealed class DeviceIpImportResult
{
    public int TotalCount { get; init; }
    public int InsertedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int SkippedManualCount { get; init; }
}