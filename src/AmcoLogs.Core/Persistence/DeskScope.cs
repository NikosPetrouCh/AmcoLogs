namespace AmcoLogs.Core.Persistence;

public static class DeskScope
{
    public static string TableKey(string? operatorName, string? network)
    {
        if (string.Equals(operatorName, "OSETH", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(network))
            {
                throw new InvalidOperationException("Το OSETH δεν έχει τραμ ή μετρό.");
            }

            return "oseth";
        }

        if (string.Equals(operatorName, "OASA", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(network, "TRAM", StringComparison.OrdinalIgnoreCase))
            {
                return "oasa_tram";
            }

            if (string.Equals(network, "METRO", StringComparison.OrdinalIgnoreCase))
            {
                return "oasa_metro";
            }

            throw new InvalidOperationException("Στο OASA διάλεξε TRAM ή METRO.");
        }

        throw new InvalidOperationException("Διάλεξε OSETH ή OASA.");
    }
}

public sealed class DeskCaseSummary
{
    public Guid Id { get; init; }
    public string Origin { get; init; } = "excel";
    public string? FileName { get; init; }
    public DateOnly? FileDate { get; init; }
    public DateTime ImportedAt { get; init; }
    public string? ServiceAccountId { get; init; }
    public string? FaultType { get; init; }
    public int Code02Count { get; init; }
    public int Code03Count { get; init; }
    public int YesCount { get; init; }
    public int NoCount { get; init; }
    public bool LogsDownloaded { get; init; }
    public string InvestigationStatus { get; init; } = "open";
    public string? Conclusion { get; init; }
}

public sealed class DeskCaseLine
{
    public Guid Id { get; init; }
    public Guid CaseId { get; init; }
    public int RowNumber { get; init; }
    public string? ServiceAccountId { get; init; }
    public string? FaultType { get; init; }
    public string? ResultCode { get; init; }
    public string? Decision { get; init; }
    public bool LogsDownloaded { get; init; }
    public string InvestigationStatus { get; init; } = "open";
    public string? Conclusion { get; init; }
    public string? TransactionId { get; init; }
    public DateTime? TransactionDate { get; init; }
    public decimal? Amount { get; init; }
    public string? TerminalCode { get; init; }
    public string? StationName { get; init; }
    public string? FileName { get; init; }
    public DateOnly? FileDate { get; init; }
    public string Origin { get; init; } = "excel";
}

public sealed class DeskCaseDraft
{
    public string Operator { get; set; } = "OSETH";
    public string? Network { get; set; }
    public string Origin { get; set; } = "excel";
    public string? FileName { get; set; }
    public DateOnly? FileDate { get; set; }
    public string? FaultType { get; set; }
    public string? ServiceAccountId { get; set; }
    public List<DeskLineDraft> Lines { get; set; } = [];
}

public sealed class DeskLineDraft
{
    public int RowNumber { get; set; }
    public string? ServiceAccountId { get; set; }
    public string? FaultType { get; set; }
    public string? ResultCode { get; set; }
    public string? Decision { get; set; }
    public string? TransactionId { get; set; }
    public DateTime? TransactionDate { get; set; }
    public decimal? Amount { get; set; }
    public string? TerminalCode { get; set; }
    public string? StationName { get; set; }
}

public sealed class DeskCasePatch
{
    public string Operator { get; set; } = "OSETH";
    public string? Network { get; set; }
    public DateOnly? FileDate { get; set; }
    public bool HasFileDate { get; set; }
    public string? FaultType { get; set; }
    public bool HasFaultType { get; set; }
    public string? Conclusion { get; set; }
    public bool HasConclusion { get; set; }
    public string? InvestigationStatus { get; set; }
    public bool HasInvestigationStatus { get; set; }
}

public sealed class DeskLinePatch
{
    public string Operator { get; set; } = "OSETH";
    public string? Network { get; set; }
    public bool? LogsDownloaded { get; set; }
    public string? FaultType { get; set; }
    public bool HasFaultType { get; set; }
    public string? Conclusion { get; set; }
    public bool HasConclusion { get; set; }
    public string? InvestigationStatus { get; set; }
    public bool HasInvestigationStatus { get; set; }
}

public sealed class DeskIpRow
{
    public string ServiceAccountId { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string? BusNo { get; set; }
    public string? TerminalCode { get; set; }
    public string? StationName { get; set; }
    public string Mobility { get; set; } = "Unknown";
    public string Source { get; set; } = "manual";
    public DateTime UpdatedAt { get; set; }
    public string Operator { get; set; } = "OSETH";
    public string? Network { get; set; }
}

public sealed class DeskErrorEntry
{
    public Guid Id { get; set; }
    public string ServiceAccountId { get; set; } = "";
    public string ErrorKind { get; set; } = "";
    public DateOnly OccurredOn { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Operator { get; set; } = "OSETH";
    public string? Network { get; set; }
}
