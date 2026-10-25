namespace AmcoLogs.Core.Excel;

public sealed class ExcelBatchTitle
{
    public required string Label { get; init; }
    public DateOnly? FileDate { get; init; }

    public static ExcelBatchTitle Parse(string? fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? string.Empty).Trim();
        var parts = stem.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2 && TryYyMmDd(parts[^1], out var date))
        {
            var name = string.Join(' ', parts.Skip(1).Take(parts.Length - 2));
            if (string.IsNullOrWhiteSpace(name))
            {
                name = parts[0];
            }

            return new ExcelBatchTitle
            {
                Label = $"{name} {date:dd/MM/yyyy}",
                FileDate = date
            };
        }

        return new ExcelBatchTitle
        {
            Label = string.IsNullOrWhiteSpace(stem) ? "Excel" : stem,
            FileDate = null
        };
    }

    private static bool TryYyMmDd(string text, out DateOnly date)
    {
        date = default;
        if (text.Length != 6 || text.Any(character => !char.IsDigit(character)))
        {
            return false;
        }

        var year = 2000 + int.Parse(text[..2]);
        var month = int.Parse(text.Substring(2, 2));
        var day = int.Parse(text.Substring(4, 2));
        try
        {
            date = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
