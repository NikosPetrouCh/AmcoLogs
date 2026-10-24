using AmcoLogs.Core.Device;

namespace AmcoLogs.Core.Excel;

public sealed class ExcelImportRow
{
    public int RowNumber { get; init; }
    public ImmediateTransactionFacts Immediate { get; init; } = new();
    public ArchiveDeviceFacts Archive { get; init; } = new();
    public RowDisposition Disposition { get; init; } = RowDisposition.Unclassified;
    public string? Decision { get; init; }
    public string? ScreenReason { get; init; }

    public DeviceMobility Mobility => DeviceCode.Classify(Immediate.ServiceAccountId);
    public bool HasServiceAccountId => !string.IsNullOrWhiteSpace(Immediate.ServiceAccountId);

    public ExcelImportRow WithScreen(RowDisposition disposition, string? decision, string reason) => new()
    {
        RowNumber = RowNumber,
        Immediate = Immediate,
        Archive = Archive,
        Disposition = disposition,
        Decision = decision,
        ScreenReason = reason
    };
}
