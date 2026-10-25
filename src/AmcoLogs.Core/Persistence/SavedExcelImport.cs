namespace AmcoLogs.Core.Persistence;

public sealed class SavedExcelImport
{
    public required Guid BatchId { get; init; }
    public required string FileName { get; init; }
    public required string Title { get; init; }
    public DateOnly? FileDate { get; init; }
    public required DateTime ImportedAt { get; init; }
    public required int RowCount { get; init; }
    public int YesCount { get; init; }
    public int NoCount { get; init; }
    public int PendingCount { get; init; }
}
