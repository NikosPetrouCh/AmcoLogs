using System.Globalization;
using AmcoLogs.Core.Device;

namespace AmcoLogs.Core.Excel;

public static class TransactionScreen
{
    public const string Yes = "YES";

    public static ExcelImportResult Apply(ExcelImportResult import)
    {
        return new ExcelImportResult
        {
            FileName = import.FileName,
            Profile = import.Profile,
            Rows = import.Rows.Select(Screen).ToArray(),
            RecognizedHeaders = import.RecognizedHeaders,
            ArchiveExtraHeaders = import.ArchiveExtraHeaders,
            Warnings = import.Warnings
        };
    }

    public const string Pending = "PENDING";

    public static ExcelImportResult ForStorage(ExcelImportResult import)
    {
        var rows = import.Rows
            .Where(row => row.Disposition is RowDisposition.AutomaticYes or RowDisposition.NeedsIp)
            .Select(row => row.Disposition == RowDisposition.NeedsIp
                ? row.WithScreen(RowDisposition.NeedsIp, Pending, row.ScreenReason ?? "Εκκρεμεί.")
                : row)
            .ToArray();

        return new ExcelImportResult
        {
            FileName = import.FileName,
            Profile = import.Profile,
            Rows = rows,
            RecognizedHeaders = import.RecognizedHeaders,
            ArchiveExtraHeaders = import.ArchiveExtraHeaders,
            Warnings = import.Warnings
        };
    }

    public static ExcelImportResult WithoutExcluded(ExcelImportResult import)
    {
        return new ExcelImportResult
        {
            FileName = import.FileName,
            Profile = import.Profile,
            Rows = import.Rows.Where(row => row.Disposition is not RowDisposition.ExcludedInvalidServiceAccount
                and not RowDisposition.ExcludedType04).ToArray(),
            RecognizedHeaders = import.RecognizedHeaders,
            ArchiveExtraHeaders = import.ArchiveExtraHeaders,
            Warnings = import.Warnings
        };
    }

    public static ExcelImportRow Screen(ExcelImportRow row)
    {
        if (!DeviceCode.IsCtvmCode(row.Immediate.ServiceAccountId))
        {
            return row.WithScreen(
                RowDisposition.ExcludedInvalidServiceAccount,
                null,
                "Το ServiceAccountID δεν είναι CTVM και 8 ψηφία.");
        }

        var result = ResultCode(row.Immediate.EticketTransactionResult);
        if (result == "04")
        {
            return row.WithScreen(RowDisposition.ExcludedType04, null, "Τύπος 04. Εξαιρείται.");
        }

        if (result == "03")
        {
            return row.WithScreen(RowDisposition.AutomaticYes, Yes, "Τύπος 03. Αυτόματο YES.");
        }

        if (result == "02" && IsAcceptedSmallAmount(row.Immediate.Amount))
        {
            return row.WithScreen(
                RowDisposition.AutomaticYes,
                Yes,
                "Τύπος 02 με αποδεκτό μικρό ποσό. Αυτόματο YES.");
        }

        if (result == "02")
        {
            return row.WithScreen(RowDisposition.NeedsIp, null, "Τύπος 02. Χρειάζεται IP.");
        }

        return row.WithScreen(RowDisposition.Unclassified, null, "Δεν υπάρχει κανόνας YES/NO για αυτόν τον τύπο.");
    }

    private static bool IsAcceptedSmallAmount(decimal? amount)
    {
        if (amount is null)
        {
            return false;
        }

        var value = amount.Value;

        return value == 1m
            || value == 2m
            || value == 0.01m
            || value == 0.02m;
    }
    private static string? ResultCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed is >= 0 and <= 99)
        {
            return parsed.ToString("00", CultureInfo.InvariantCulture);
        }

        return text;
    }
}
