namespace AmcoLogs.Core.Persistence;

public sealed class SavedExcelImport
{
    public required Guid BatchId { get; init; }
    public required string FileName { get; init; }
    public required DateTime ImportedAt { get; init; }
    public required int RowCount { get; init; }
}
