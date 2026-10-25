using System.Globalization;
using System.Text.RegularExpressions;
using AmcoLogs.Core.Logs;

namespace AmcoLogs.Infrastructure.Logs;

public sealed class NlogSliceReader
{
    private const int MaxBodyLines = 800;
    private static readonly Regex LinePattern = new(
        @"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)\|",
        RegexOptions.Compiled);
    private static readonly Regex TransactionIdPattern = new(
        @"Creating new transaction with id:\s*([0-9a-fA-F-]{36})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExtraTransactionIdPattern = new(
        @"[""']TransactionId[""']\s*:\s*[""']([0-9a-fA-F-]{36})[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public LogSliceResult Read(string path, string transactionId, DateTime? excelTime)
    {
        var wanted = transactionId.Trim();
        var files = NlogFile.Enumerate(path);
        if (files.Count == 0)
        {
            return Missing("Δεν υπάρχει αρχείο log.");
        }

        var day = excelTime?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ordered = files
            .OrderByDescending(file => string.Equals(NlogFile.DayFromName(file), day, StringComparison.Ordinal))
            .ToArray();

        Candidate? best = null;
        foreach (var file in ordered)
        {
            if (day is not null
                && best is not null
                && string.Equals(NlogFile.DayFromName(file), day, StringComparison.Ordinal) == false
                && string.Equals(NlogFile.DayFromName(ordered[0]), day, StringComparison.Ordinal))
            {
                break;
            }

            using var stream = NlogFile.OpenRead(file);
            var hit = Scan(stream, wanted, excelTime, file);
            if (hit is null)
            {
                continue;
            }

            if (best is null || hit.Value.Seconds < best.Value.Seconds)
            {
                best = hit;
            }
        }

        return best?.Result ?? Missing("Δε βρέθηκε η συναλλαγή.");
    }

    private static Candidate? Scan(Stream log, string wanted, DateTime? excelTime, string file)
    {
        Candidate? best = null;
        Window? open = null;
        using var reader = new StreamReader(log);
        var lineNumber = 0;
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (!IsSelectingProduct(raw))
            {
                open?.Add(lineNumber, raw);
                continue;
            }

            if (open is not null)
            {
                best = Consider(open, wanted, excelTime, file, raw, lineNumber, best);
            }

            open = Window.Start(lineNumber, raw);
        }

        if (open is not null)
        {
            best = Consider(open, wanted, excelTime, file, null, 0, best);
        }

        return best;
    }

    private static Candidate? Consider(
        Window open,
        string wanted,
        DateTime? excelTime,
        string file,
        string? nextSelecting,
        int nextLine,
        Candidate? best)
    {
        if (!open.Contains(wanted))
        {
            return best;
        }

        var lines = new List<LogSliceLine>
        {
            new() { LineNumber = open.LineStart, Text = open.StartText, Role = "start" }
        };
        lines.AddRange(open.Body.Select(line => new LogSliceLine
        {
            LineNumber = line.Number,
            Text = line.Text,
            Role = "between"
        }));
        if (nextSelecting is not null)
        {
            lines.Add(new LogSliceLine
            {
                LineNumber = nextLine,
                Text = nextSelecting,
                Role = "end"
            });
        }

        var distance = Distance(open.StartTime, excelTime);
        var result = new LogSliceResult
        {
            Found = true,
            Message = SliceMessage(nextSelecting is not null, open.Truncated),
            LogFileName = Path.GetFileName(file),
            LineStart = open.LineStart,
            LineEnd = nextSelecting is null ? open.LastLine : nextLine,
            Lines = lines
        };
        if (best is null || distance < best.Value.Seconds)
        {
            return new Candidate(distance, result);
        }

        return best;
    }

    private static string SliceMessage(bool hasNext, bool truncated)
    {
        var message = hasNext
            ? "Γραμμές από το Selecting product μέχρι το επόμενο."
            : "Βρέθηκε το Transaction ID. Δεν υπάρχει επόμενο Selecting product.";
        return truncated
            ? message + " Το κομμάτι κόπηκε στις 800 γραμμές."
            : message;
    }

    private static LogSliceResult Missing(string message) => new()
    {
        Found = false,
        Message = message,
        Lines = []
    };

    private static bool IsSelectingProduct(string raw)
    {
        var message = MessageOf(raw);
        return message.StartsWith("Selecting product", StringComparison.OrdinalIgnoreCase);
    }

    private static string MessageOf(string raw)
    {
        var first = raw.IndexOf('|');
        if (first < 0)
        {
            return raw.Trim();
        }

        var second = raw.IndexOf('|', first + 1);
        if (second < 0)
        {
            return raw.Trim();
        }

        var third = raw.IndexOf('|', second + 1);
        return third < 0 ? raw.Trim() : raw[(third + 1)..].Trim();
    }

    private static double Distance(DateTime? start, DateTime? excel)
    {
        if (start is null || excel is null)
        {
            return double.MaxValue;
        }

        return Math.Abs((start.Value - excel.Value).TotalSeconds);
    }

    private static DateTime? TimeOf(string raw)
    {
        var match = LinePattern.Match(raw);
        if (!match.Success)
        {
            return null;
        }

        return DateTime.TryParseExact(
            match.Groups["time"].Value,
            "yyyy-MM-dd HH:mm:ss.ffff",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private sealed class Window
    {
        public int LineStart { get; private init; }
        public int LastLine { get; private set; }
        public string StartText { get; private init; } = "";
        public DateTime? StartTime { get; private init; }
        public bool Truncated { get; private set; }
        public List<NumberedLine> Body { get; } = [];

        public static Window Start(int lineNumber, string raw) => new()
        {
            LineStart = lineNumber,
            LastLine = lineNumber,
            StartText = raw,
            StartTime = TimeOf(raw)
        };

        public void Add(int lineNumber, string raw)
        {
            if (Body.Count >= MaxBodyLines)
            {
                Truncated = true;
                return;
            }

            LastLine = lineNumber;
            Body.Add(new NumberedLine(lineNumber, raw));
        }

        public bool Contains(string wanted)
        {
            foreach (var line in Body)
            {
                var message = MessageOf(line.Text);
                var created = TransactionIdPattern.Match(message);
                if (created.Success && string.Equals(created.Groups[1].Value, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var extra = ExtraTransactionIdPattern.Match(message);
                if (extra.Success && string.Equals(extra.Groups[1].Value, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private readonly record struct NumberedLine(int Number, string Text);
    private readonly record struct Candidate(double Seconds, LogSliceResult Result);
}
