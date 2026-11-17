using System.Globalization;

namespace AmcoLogs.Core.Logs;

public static class TransactionClock
{
    public static bool TryParse(string? text, out TimeOnly clock)
    {
        clock = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        return TimeOnly.TryParseExact(
            trimmed,
            ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out clock);
    }

    public static string? FromOsethTransactionId(string? transactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return null;
        }

        var digits = new string(transactionId.Where(char.IsDigit).ToArray());
        if (digits.Length < 9)
        {
            return null;
        }

        var hourText = digits.Substring(5, 2);
        var minuteText = digits.Substring(7, 2);
        if (!int.TryParse(hourText, NumberStyles.None, CultureInfo.InvariantCulture, out var hour) || hour > 23)
        {
            return null;
        }

        if (!int.TryParse(minuteText, NumberStyles.None, CultureInfo.InvariantCulture, out var minute) || minute > 59)
        {
            return null;
        }

        return $"{hour:00}:{minute:00}";
    }

    public static string? FromTimestamp(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}
