using System.Text.RegularExpressions;
using AmcoLogs.Core.Logs;

namespace AmcoLogs.Infrastructure.Logs;

public sealed class NlogSaleLocator : INlogSaleLocator
{
    private const int MaxWindowLines = 250;
    private static readonly Regex LinePattern = new(
        @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)\|(?<level>\w+)\s+\|(?<logger>[^|]+)\|(?<message>.*)$",
        RegexOptions.Compiled);
    private static readonly Regex TransactionIdPattern = new(
        @"Creating new transaction with id:\s*([0-9a-fA-F-]{36})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExtraTransactionIdPattern = new(
        @"[""']TransactionId[""']\s*:\s*[""']([0-9a-fA-F-]{36})[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public LogInspectResult Inspect(Stream log, IReadOnlyCollection<string> transactionIds)
    {
        var wanted = new HashSet<string>(
            transactionIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(NormalizeId),
            StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            return new LogInspectResult
            {
                SelectingProductCount = 0,
                CardSaleCount = 0,
                MatchedCount = 0,
                NotFoundTransactionIds = [],
                Sales = []
            };
        }

        var sales = new List<CardSaleWindow>();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectingCount = 0;
        var cardCount = 0;

        SaleBuffer? open = null;
        string? pendingError = null;
        int pendingErrorLine = 0;
        string pendingErrorTime = "";
        string pendingErrorLogger = "";

        using var reader = new StreamReader(log);
        var lineNumber = 0;
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            lineNumber++;
            var match = LinePattern.Match(raw);
            if (!match.Success)
            {
                if (pendingError is not null && pendingError.Length < 800)
                {
                    pendingError += " " + raw.Trim();
                }

                continue;
            }

            FlushPendingError(open, ref pendingError, pendingErrorLine, pendingErrorTime, pendingErrorLogger);

            var level = match.Groups["level"].Value.Trim();
            var logger = match.Groups["logger"].Value.Trim();
            var message = match.Groups["message"].Value.Trim();
            var time = match.Groups["time"].Value;

            if (message.StartsWith("Selecting product", StringComparison.OrdinalIgnoreCase))
            {
                selectingCount++;
                FlushPendingError(open, ref pendingError, pendingErrorLine, pendingErrorTime, pendingErrorLogger);
                CloseWindow(open, wanted, found, sales, ref cardCount);
                open = new SaleBuffer
                {
                    LineStart = lineNumber,
                    StartTime = time,
                    Product = message["Selecting product".Length..].Trim()
                };
            }

            if (open is null)
            {
                continue;
            }

            open.LineEnd = lineNumber;
            open.LineCount++;

            if (message.Contains("Setting payment method to: Card", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Payment Method: (Card)", StringComparison.OrdinalIgnoreCase))
            {
                open.IsCard = true;
            }

            if (message.Contains("Setting payment method to: Cash", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Payment Method: (Cash)", StringComparison.OrdinalIgnoreCase))
            {
                open.IsCash = true;
            }

            var idMatch = TransactionIdPattern.Match(message);
            if (idMatch.Success)
            {
                open.TransactionId = NormalizeId(idMatch.Groups[1].Value);
            }
            else if (string.IsNullOrWhiteSpace(open.TransactionId))
            {
                var extraId = ExtraTransactionIdPattern.Match(message);
                if (extraId.Success)
                {
                    open.TransactionId = NormalizeId(extraId.Groups[1].Value);
                }
            }

            if (string.Equals(level, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                pendingError = message;
                pendingErrorLine = lineNumber;
                pendingErrorTime = time;
                pendingErrorLogger = logger;
            }

            if (open.LineCount >= MaxWindowLines)
            {
                FlushPendingError(open, ref pendingError, pendingErrorLine, pendingErrorTime, pendingErrorLogger);
                CloseWindow(open, wanted, found, sales, ref cardCount);
                open = null;
            }
        }

        FlushPendingError(open, ref pendingError, pendingErrorLine, pendingErrorTime, pendingErrorLogger);
        CloseWindow(open, wanted, found, sales, ref cardCount);

        var notFound = wanted.Where(id => !found.Contains(id)).OrderBy(id => id).ToArray();

        return new LogInspectResult
        {
            SelectingProductCount = selectingCount,
            CardSaleCount = cardCount,
            MatchedCount = sales.Count,
            NotFoundTransactionIds = notFound,
            Sales = sales
        };
    }

    private static void CloseWindow(
        SaleBuffer? open,
        HashSet<string> wanted,
        HashSet<string> found,
        List<CardSaleWindow> sales,
        ref int cardCount)
    {
        if (open is null)
        {
            return;
        }

        if (!open.IsCard || open.IsCash || string.IsNullOrWhiteSpace(open.TransactionId))
        {
            return;
        }

        cardCount++;
        if (!wanted.Contains(open.TransactionId))
        {
            return;
        }

        found.Add(open.TransactionId);
        sales.Add(new CardSaleWindow
        {
            TransactionId = open.TransactionId,
            Product = open.Product,
            StartTime = open.StartTime,
            LineStart = open.LineStart,
            LineEnd = open.LineEnd,
            LineCount = open.LineCount,
            Errors = open.Errors
        });
    }

    private static void FlushPendingError(
        SaleBuffer? open,
        ref string? pendingError,
        int line,
        string time,
        string logger)
    {
        if (open is null || pendingError is null)
        {
            pendingError = null;
            return;
        }

        open.Errors.Add(new SaleError
        {
            LineNumber = line,
            Time = time,
            Logger = logger,
            Message = pendingError
        });
        pendingError = null;
    }

    private static string NormalizeId(string value) => value.Trim();

    private sealed class SaleBuffer
    {
        public int LineStart { get; init; }
        public int LineEnd { get; set; }
        public int LineCount { get; set; }
        public string? StartTime { get; init; }
        public string? Product { get; init; }
        public string? TransactionId { get; set; }
        public bool IsCard { get; set; }
        public bool IsCash { get; set; }
        public List<SaleError> Errors { get; } = [];
    }
}
