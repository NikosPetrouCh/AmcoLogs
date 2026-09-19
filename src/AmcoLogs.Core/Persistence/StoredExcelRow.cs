namespace AmcoLogs.Core.Persistence;

public sealed class StoredExcelRow
{
    public required Guid BatchId { get; init; }
    public required string FileName { get; init; }
    public required DateTime ImportedAt { get; init; }
    public required int RowNumber { get; init; }
    public string? ServiceAccountId { get; init; }
    public string? Rrn { get; init; }
    public decimal? Amount { get; init; }
    public string? EticketTransactionResult { get; init; }
    public string? TransactionId { get; init; }
    public DateTime? TransactionDate { get; init; }
    public required string Mobility { get; init; }
    public string? ExtraJson { get; init; }
}
