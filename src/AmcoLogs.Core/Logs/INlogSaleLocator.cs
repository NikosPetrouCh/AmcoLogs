namespace AmcoLogs.Core.Logs;

public sealed class SaleError
{
    public int LineNumber { get; init; }
    public string Time { get; init; } = "";
    public string Logger { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class CardSaleWindow
{
    public required string TransactionId { get; init; }
    public string? Product { get; init; }
    public string? StartTime { get; init; }
    public int LineStart { get; init; }
    public int LineEnd { get; init; }
    public int LineCount { get; init; }
    public IReadOnlyList<SaleError> Errors { get; init; } = [];
}

public sealed class LogInspectResult
{
    public int SelectingProductCount { get; init; }
    public int CardSaleCount { get; init; }
    public int MatchedCount { get; init; }
    public IReadOnlyList<string> NotFoundTransactionIds { get; init; } = [];
    public IReadOnlyList<CardSaleWindow> Sales { get; init; } = [];
}

public interface INlogSaleLocator
{
    LogInspectResult Inspect(Stream log, IReadOnlyCollection<string> transactionIds);
}
