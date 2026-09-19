using AmcoLogs.Core.Device;

namespace AmcoLogs.Core.Excel;

public sealed class ExcelImportRow
{
    public int RowNumber { get; init; }
    public ImmediateTransactionFacts Immediate { get; init; } = new();
    public ArchiveDeviceFacts Archive { get; init; } = new();

    public DeviceMobility Mobility => DeviceCode.Classify(Immediate.ServiceAccountId);
    public bool HasServiceAccountId => !string.IsNullOrWhiteSpace(Immediate.ServiceAccountId);
}
