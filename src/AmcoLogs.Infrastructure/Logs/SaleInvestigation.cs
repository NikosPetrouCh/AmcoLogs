using AmcoLogs.Core.Logs;

namespace AmcoLogs.Infrastructure.Logs;

public sealed class SaleInvestigation
{
    private readonly INlogSaleLocator _locator;

    public SaleInvestigation(INlogSaleLocator locator)
    {
        _locator = locator;
    }

    public IReadOnlyList<InvestigationFinding> Run(
        IReadOnlyList<InvestigationSubject> subjects,
        IReadOnlyList<LogFileRef> logs)
    {
        var findings = new List<InvestigationFinding>();
        var remaining = subjects
            .Select((subject, index) => new Tracked(index, subject))
            .ToList();

        foreach (var log in logs)
        {
            foreach (var file in NlogFile.Enumerate(log.Path))
            {
                var day = NlogFile.DayFromName(file);
                var candidates = remaining
                    .Where(item => SameDevice(item.Subject.ServiceAccountId, log.ServiceAccountId)
                        && DayMatches(item.Subject.TransactionDate, day))
                    .ToList();
                if (candidates.Count == 0)
                {
                    continue;
                }

                var ids = candidates
                    .Select(item => item.Subject.TransactionId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (ids.Length == 0)
                {
                    continue;
                }

                using var stream = NlogFile.OpenRead(file);
                var inspect = _locator.Inspect(stream, ids);
                var sales = inspect.Sales.ToDictionary(sale => sale.TransactionId, StringComparer.OrdinalIgnoreCase);

                foreach (var candidate in candidates)
                {
                    var id = candidate.Subject.TransactionId;
                    if (string.IsNullOrWhiteSpace(id) || !sales.TryGetValue(id, out var sale))
                    {
                        continue;
                    }

                    remaining.Remove(candidate);
                    findings.Add(ToFinding(candidate.Subject, sale, file, day));
                }
            }
        }

        foreach (var leftover in remaining)
        {
            var hasLog = logs.Any(log =>
                SameDevice(leftover.Subject.ServiceAccountId, log.ServiceAccountId)
                && NlogFile.Enumerate(log.Path).Any(file =>
                    DayMatches(leftover.Subject.TransactionDate, NlogFile.DayFromName(file))));
            findings.Add(new InvestigationFinding
            {
                TransactionId = leftover.Subject.TransactionId,
                Rrn = leftover.Subject.Rrn,
                Amount = leftover.Subject.Amount,
                TransactionDate = leftover.Subject.TransactionDate,
                ServiceAccountId = leftover.Subject.ServiceAccountId,
                Mobility = leftover.Subject.Mobility,
                Verdict = hasLog ? InvestigationVerdict.NotFoundInLog : InvestigationVerdict.NoLogFile
            });
        }

        return findings
            .OrderBy(item => item.TransactionDate)
            .ThenBy(item => item.TransactionId)
            .ToArray();
    }

    private static InvestigationFinding ToFinding(
        InvestigationSubject subject,
        CardSaleWindow sale,
        string file,
        string? day) =>
        new()
        {
            TransactionId = subject.TransactionId,
            Rrn = subject.Rrn,
            Amount = subject.Amount,
            TransactionDate = subject.TransactionDate,
            ServiceAccountId = subject.ServiceAccountId,
            Mobility = subject.Mobility,
            Product = sale.Product,
            LogFileName = Path.GetFileName(file),
            LogDay = day,
            LineStart = sale.LineStart,
            LineEnd = sale.LineEnd,
            LineCount = sale.LineCount,
            ErrorCount = sale.Errors.Count,
            Verdict = sale.Errors.Count > 0
                ? InvestigationVerdict.LocatedWithErrors
                : InvestigationVerdict.LocatedNoErrors,
            Errors = sale.Errors
        };

    private static bool SameDevice(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool DayMatches(DateTime? transactionDate, string? logDay)
    {
        if (string.IsNullOrWhiteSpace(logDay) || transactionDate is null)
        {
            return true;
        }

        return transactionDate.Value.ToString("yyyy-MM-dd") == logDay;
    }

    private sealed record Tracked(int Index, InvestigationSubject Subject);
}
