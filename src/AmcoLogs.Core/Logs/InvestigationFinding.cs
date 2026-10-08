namespace AmcoLogs.Core.Logs;

public static class InvestigationVerdict
{
    public const string LocatedNoErrors = "LocatedNoErrors";
    public const string LocatedWithErrors = "LocatedWithErrors";
    public const string NotFoundInLog = "NotFoundInLog";
    public const string NoLogFile = "NoLogFile";
}

public sealed class InvestigationSubject
{
    public string? TransactionId { get; init; }
    public string? Rrn { get; init; }
    public decimal? Amount { get; init; }
    public DateTime? TransactionDate { get; init; }
    public string? ServiceAccountId { get; init; }
    public string Mobility { get; init; } = "Unknown";
}

public sealed class LogFileRef
{
    public required string ServiceAccountId { get; init; }
    public required string Path { get; init; }
}

public sealed class InvestigationFinding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? TransactionId { get; init; }
    public string? Rrn { get; init; }
    public decimal? Amount { get; init; }
    public DateTime? TransactionDate { get; init; }
    public string? ServiceAccountId { get; init; }
    public string Mobility { get; init; } = "Unknown";
    public string? Product { get; init; }
    public string? LogFileName { get; init; }
    public string? LogDay { get; init; }
    public int? LineStart { get; init; }
    public int? LineEnd { get; init; }
    public int LineCount { get; init; }
    public int ErrorCount { get; init; }
    public string Verdict { get; init; } = InvestigationVerdict.NoLogFile;
    public IReadOnlyList<SaleError> Errors { get; init; } = [];
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public sealed class InvestigationRunResult
{
    public int SubjectCount { get; init; }
    public int LogFileCount { get; init; }
    public int LocatedCount { get; init; }
    public int ErrorSaleCount { get; init; }
    public int NotFoundCount { get; init; }
    public int MissingLogCount { get; init; }
    public bool Persisted { get; init; }
    public string? PersistMessage { get; init; }
    public IReadOnlyList<InvestigationFinding> Findings { get; init; } = [];
}
