namespace AmcoLogs.Core.Excel;

/// <summary>
/// Excel fields used immediately to identify a device and a transaction.
/// </summary>
public sealed class ImmediateTransactionFacts
{
    public string? ServiceAccountId { get; init; }
    public string? Rrn { get; init; }
    public decimal? Amount { get; init; }
    public string? EticketTransactionResult { get; init; }
    public string? TransactionId { get; init; }
    public DateTime? TransactionDate { get; init; }
}
