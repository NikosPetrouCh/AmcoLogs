
using System.Text.RegularExpressions;
using AmcoLogs.Core.Logs;

namespace AmcoLogs.Infrastructure.Logs;

public sealed class NlogSaleLocator : INlogSaleLocator
{
    private static readonly Regex LinePattern = new(
        @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d+)?)\s*\|\s*(?<level>\w+)\s*\|\s*(?<logger>[^|]+)\|\s*(?<message>.*)$",
        RegexOptions.Compiled);

    // Numeric Transaction ID from the MQTT debitStart payload.
    private static readonly Regex TxnIdPattern = new(
        @"""?TxnId""?\s*:\s*""?(\d+)""?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public LogInspectResult Inspect(
        Stream log,
        IReadOnlyCollection<string> transactionIds)
    {
        var wanted = new HashSet<string>(
            transactionIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(NormalizeId),
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

        var found = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

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

            // Multiline MQTT payload:
            // TxnId may appear on a continuation line
            // that does not have the standard NLog prefix.
            if (!match.Success)
            {
                if (open is not null)
                {
                    CaptureTransactionId(raw, open, wanted);

                    open.LineEnd = lineNumber;
                    open.LineCount++;
                }

                if (pendingError is not null &&
                    pendingError.Length < 800)
                {
                    pendingError += " " + raw.Trim();
                }

                continue;
            }

            FlushPendingError(
                open,
                ref pendingError,
                pendingErrorLine,
                pendingErrorTime,
                pendingErrorLogger);

            var level = match.Groups["level"].Value.Trim();
            var logger = match.Groups["logger"].Value.Trim();
            var message = match.Groups["message"].Value.Trim();
            var time = match.Groups["time"].Value;

            // Every Selecting product starts a new transaction window.
            // The previous window ends immediately before this one.
            if (message.StartsWith(
                "Selecting product",
                StringComparison.OrdinalIgnoreCase))
            {
                selectingCount++;

                FlushPendingError(
                    open,
                    ref pendingError,
                    pendingErrorLine,
                    pendingErrorTime,
                    pendingErrorLogger);

                CloseWindow(
                    open,
                    wanted,
                    found,
                    sales,
                    ref cardCount);

                open = new SaleBuffer
                {
                    LineStart = lineNumber,
                    LineEnd = lineNumber,
                    StartTime = time,
                    Product = message[
                        "Selecting product".Length..].Trim()
                };
            }

            if (open is null)
            {
                continue;
            }

            open.LineEnd = lineNumber;
            open.LineCount++;

            // Payment method information.
            if (message.Contains(
                "Setting payment method to: Card",
                StringComparison.OrdinalIgnoreCase)
                || message.Contains(
                "Payment Method: (Card)",
                StringComparison.OrdinalIgnoreCase))
            {
                open.IsCard = true;
            }

            if (message.Contains(
                "Setting payment method to: Cash",
                StringComparison.OrdinalIgnoreCase)
                || message.Contains(
                "Payment Method: (Cash)",
                StringComparison.OrdinalIgnoreCase))
            {
                open.IsCash = true;
            }

            // Find the numeric Transaction ID.
            CaptureTransactionId(raw, open, wanted);

            // Keep errors associated with the current product window.
            if (string.Equals(
                level,
                "ERROR",
                StringComparison.OrdinalIgnoreCase))
            {
                pendingError = message;
                pendingErrorLine = lineNumber;
                pendingErrorTime = time;
                pendingErrorLogger = logger;
            }
        }

        // Close the last transaction window at EOF.
        FlushPendingError(
            open,
            ref pendingError,
            pendingErrorLine,
            pendingErrorTime,
            pendingErrorLogger);

        CloseWindow(
            open,
            wanted,
            found,
            sales,
            ref cardCount);

        var notFound = wanted
            .Where(id => !found.Contains(id))
            .OrderBy(id => id)
            .ToArray();

        return new LogInspectResult
        {
            SelectingProductCount = selectingCount,
            CardSaleCount = cardCount,
            MatchedCount = sales.Count,
            NotFoundTransactionIds = notFound,
            Sales = sales
        };
    }

    private static void CaptureTransactionId(
        string text,
        SaleBuffer open,
        HashSet<string> wanted)
    {
        var match = TxnIdPattern.Match(text);

        if (!match.Success)
        {
            return;
        }

        var transactionId = NormalizeId(
            match.Groups[1].Value);

        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return;
        }

        // Prefer an ID that actually exists in the Excel input.
        // This prevents an unrelated TxnId from replacing
        // the transaction we are looking for.
        if (wanted.Contains(transactionId))
        {
            open.TransactionId = transactionId;
            return;
        }

        // Keep the first available ID as a fallback.
        if (string.IsNullOrWhiteSpace(open.TransactionId))
        {
            open.TransactionId = transactionId;
        }
    }

    private static void CloseWindow(
        SaleBuffer? open,
        HashSet<string> wanted,
        HashSet<string> found,
        List<CardSaleWindow> sales,
        ref int cardCount)
    {
        if (open is null ||
            string.IsNullOrWhiteSpace(open.TransactionId))
        {
            return;
        }

        // Count actual card windows separately.
        if (open.IsCard && !open.IsCash)
        {
            cardCount++;
        }

        // Transaction ID is the primary matching criterion.
        // Do not reject a matching transaction just because
        // the payment-method log message was not detected.
        if (!wanted.Contains(open.TransactionId))
        {
            return;
        }

        // Avoid duplicate results for the same Transaction ID.
        if (!found.Add(open.TransactionId))
        {
            return;
        }

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

    private static string NormalizeId(string value)
    {
        return value.Trim();
    }

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
