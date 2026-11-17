using System.Globalization;
using System.Net;
using System.Text;
using AmcoLogs.Core.Device;
using AmcoLogs.Core.Excel;
using ClosedXML.Excel;

namespace AmcoLogs.Infrastructure.Excel;

public sealed class ClosedXmlExcelImportParser : IExcelImportParser
{
    private static readonly string[] ServiceAccountAliases = ["serviceaccountid", "serviceaccount"];
    private static readonly string[] RrnAliases = ["rrn"];
    private static readonly string[] AmountAliases = ["amount", "ποσο"];
    private static readonly string[] ResultAliases = ["etickettransactionresult"];
    private static readonly string[] TransactionIdAliases = ["transactionid"];
    private static readonly string[] TransactionDateAliases = ["transactiondate", "ημερομηνιασυναλλαγης", "ημερομηνιασυνναλαγης", "ημερομηνιασυναλαγης"];
    private static readonly string[] OasaTerminalAliases = ["οασακωδικοστερματικου", "οασακωδικοςτερματικου", "κωδικοστερματικου", "κωδικοςτερματικου"];
    private static readonly string[] OasaStatusAliases = ["κωδκαταστασης", "κωδικοςκαταστασης", "κωδκατασταση"];

    private readonly IOasaTerminalCatalog _oasa;

    public ClosedXmlExcelImportParser(IOasaTerminalCatalog oasa)
    {
        _oasa = oasa;
    }

    public ExcelImportResult Parse(Stream stream, string fileName)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("Το Excel δεν έχει φύλλα.");

        var used = worksheet.RangeUsed();
        if (used is null)
        {
            return new ExcelImportResult
            {
                FileName = fileName,
                Warnings = ["Το φύλλο είναι κενό."]
            };
        }

        var headerRow = used.FirstRow();
        var columns = new List<ColumnMap>();
        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim();
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            columns.Add(new ColumnMap(cell.Address.ColumnNumber, header, Normalize(header)));
        }

        if (Find(columns, OasaTerminalAliases) is not null)
        {
            return ParseOasa(worksheet, fileName, headerRow.RowNumber(), used.LastRow().RowNumber(), columns);
        }

        var serviceAccount = Find(columns, ServiceAccountAliases);
        var rrn = Find(columns, RrnAliases);
        var amount = Find(columns, AmountAliases);
        var result = Find(columns, ResultAliases);
        var transactionId = Find(columns, TransactionIdAliases);
        var transactionDate = Find(columns, TransactionDateAliases);

        var mapped = new HashSet<ColumnMap>();
        foreach (var column in new[] { serviceAccount, rrn, amount, result, transactionId, transactionDate })
        {
            if (column is not null)
            {
                mapped.Add(column);
            }
        }

        var extraColumns = columns.Where(column => !mapped.Contains(column)).ToList();
        var warnings = new List<string>();
        if (serviceAccount is null)
        {
            warnings.Add("Δεν βρέθηκε στήλη ServiceAccountID.");
        }

        var expected = new (ColumnMap? Column, string Name)[]
        {
            (rrn, "RRN"),
            (amount, "Amount"),
            (result, "EticketTransactionResult"),
            (transactionId, "TransactionID"),
            (transactionDate, "TransactionDate")
        };
        foreach (var (column, name) in expected)
        {
            if (column is null)
            {
                warnings.Add($"Δεν βρέθηκε στήλη {name}.");
            }
        }

        var rows = new List<ExcelImportRow>();
        var lastRow = used.LastRow().RowNumber();
        for (var rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRow; rowNumber++)
        {
            var worksheetRow = worksheet.Row(rowNumber);
            if (worksheetRow.IsEmpty())
            {
                continue;
            }

            var extra = extraColumns.ToDictionary(
                column => column.Header,
                column => NullIfEmpty(ReadString(worksheetRow, column.Index)),
                StringComparer.OrdinalIgnoreCase);

            rows.Add(new ExcelImportRow
            {
                RowNumber = rowNumber,
                Immediate = new ImmediateTransactionFacts
                {
                    ServiceAccountId = NullIfEmpty(ReadString(worksheetRow, serviceAccount?.Index)),
                    Rrn = NullIfEmpty(ReadString(worksheetRow, rrn?.Index)),
                    Amount = amount is null ? null : ReadDecimal(worksheetRow, amount.Index),
                    EticketTransactionResult = ReadResultCode(worksheetRow, result?.Index),
                    TransactionId = NullIfEmpty(ReadString(worksheetRow, transactionId?.Index)),
                    TransactionDate = transactionDate is null ? null : ReadDate(worksheetRow, transactionDate.Index)
                },
                Archive = new ArchiveDeviceFacts
                {
                    ExtraCodes = extra
                }
            });
        }

        if (rows.Count == 0)
        {
            warnings.Add("Δεν βρέθηκαν γραμμές δεδομένων κάτω από τις κεφαλίδες.");
        }

        return new ExcelImportResult
        {
            FileName = fileName,
            Profile = ExcelImportResult.OsethProfile,
            Rows = rows,
            RecognizedHeaders = mapped.Select(column => column.Header).ToArray(),
            ArchiveExtraHeaders = extraColumns.Select(column => column.Header).ToArray(),
            Warnings = warnings
        };
    }

    private ExcelImportResult ParseOasa(IXLWorksheet worksheet, string fileName, int headerRowNumber, int lastRow, List<ColumnMap> columns)
    {
        var terminal = Find(columns, OasaTerminalAliases);
        var amount = Find(columns, AmountAliases);
        var status = Find(columns, OasaStatusAliases);
        var transactionDate = Find(columns, TransactionDateAliases);
        var warnings = new List<string>();
        if (amount is null)
        {
            warnings.Add("Δεν βρέθηκε στήλη Ποσό.");
        }

        if (transactionDate is null)
        {
            warnings.Add("Δεν βρέθηκε στήλη Ημερομηνία Συναλλαγής.");
        }

        if (status is null)
        {
            warnings.Add("Δεν βρέθηκε στήλη Κωδ. Κατάστασης.");
        }

        if (_oasa.Count == 0)
        {
            warnings.Add("Το Ips_Oasa.json είναι άδειο ή λείπει. Χωρίς αυτό ο κωδικός τερματικού δεν γίνεται CTVM και IP.");
        }

        var mapped = new HashSet<ColumnMap>();
        foreach (var column in new[] { terminal, amount, status, transactionDate })
        {
            if (column is not null)
            {
                mapped.Add(column);
            }
        }

        var rows = new List<ExcelImportRow>();
        var unmatched = 0;
        for (var rowNumber = headerRowNumber + 1; rowNumber <= lastRow; rowNumber++)
        {
            var worksheetRow = worksheet.Row(rowNumber);
            if (worksheetRow.IsEmpty())
            {
                continue;
            }

            var terminalCode = NullIfEmpty(ReadString(worksheetRow, terminal?.Index));
            var hit = _oasa.Find(terminalCode);
            var ctvm = NormalizeCtvm(hit?.ServiceAccountId);
            var ip = ValidIp(hit?.IpAddress);
            var statusCode = NullIfEmpty(ReadString(worksheetRow, status?.Index));
            string reason;
            RowDisposition disposition;
            if (terminalCode is null)
            {
                disposition = RowDisposition.Unclassified;
                reason = "Κενός κωδικός τερματικού ΟΑΣΑ.";
                unmatched++;
            }
            else if (hit is null)
            {
                disposition = RowDisposition.Unclassified;
                reason = _oasa.Count == 0
                    ? "Δεν έχει φορτωθεί αντιστοιχία τερματικού."
                    : "Δεν βρέθηκε CTVM για αυτόν τον κωδικό τερματικού.";
                unmatched++;
            }
            else if (!DeviceCode.IsCtvmCode(ctvm))
            {
                disposition = RowDisposition.Unclassified;
                reason = "Η αντιστοιχία δεν έχει CTVM και 8 ψηφία.";
                unmatched++;
                ctvm = null;
                ip = null;
            }
            else if (ip is null)
            {
                disposition = RowDisposition.NeedsIp;
                reason = "Βρέθηκε CTVM, χωρίς έγκυρη IP στο Ips_Oasa.json.";
            }
            else
            {
                disposition = RowDisposition.NeedsIp;
                reason = "ΟΑΣΑ τερματικό αντιστοιχίστηκε σε CTVM και IP.";
            }

            var extra = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["OasaTerminal"] = terminalCode,
                ["StationName"] = hit?.Station,
                ["StatusCode"] = statusCode,
                ["ResolvedIp"] = ip
            };

            rows.Add(new ExcelImportRow
            {
                RowNumber = rowNumber,
                OasaTerminal = terminalCode,
                StationName = hit?.Station,
                StatusCode = statusCode,
                ResolvedIp = ip,
                Disposition = disposition,
                ScreenReason = reason,
                Immediate = new ImmediateTransactionFacts
                {
                    ServiceAccountId = ctvm,
                    Amount = amount is null ? null : ReadDecimal(worksheetRow, amount.Index),
                    TransactionDate = transactionDate is null ? null : ReadDate(worksheetRow, transactionDate.Index)
                },
                Archive = new ArchiveDeviceFacts
                {
                    ExtraCodes = extra
                }
            });
        }

        if (rows.Count == 0)
        {
            warnings.Add("Δεν βρέθηκαν γραμμές δεδομένων κάτω από τις κεφαλίδες.");
        }
        else if (unmatched > 0)
        {
            warnings.Add($"{unmatched} γραμμές χωρίς αντιστοιχία CTVM.");
        }

        return new ExcelImportResult
        {
            FileName = fileName,
            Profile = ExcelImportResult.OasaProfile,
            Rows = rows,
            RecognizedHeaders = mapped.Select(column => column.Header).ToArray(),
            Warnings = warnings
        };
    }

    private static string? NormalizeCtvm(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = DeviceCode.Digits(raw);
        if (digits.Length == 8)
        {
            return "CTVM" + digits;
        }

        return DeviceCode.IsCtvmCode(raw) ? "CTVM" + digits : raw.Trim();
    }

    private static string? ValidIp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return IPAddress.TryParse(raw.Trim(), out _) ? raw.Trim() : null;
    }

    private static ColumnMap? Find(IEnumerable<ColumnMap> columns, IEnumerable<string> aliases)
    {
        var aliasSet = aliases.ToHashSet(StringComparer.Ordinal);
        return columns.FirstOrDefault(column => aliasSet.Contains(column.Normalized));
    }

    private static string ReadString(IXLRow row, int? column)
    {
        if (column is null)
        {
            return string.Empty;
        }

        var cell = row.Cell(column.Value);
        if (cell.DataType == XLDataType.DateTime)
        {
            return cell.GetDateTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (cell.DataType == XLDataType.Number)
        {
            return cell.GetDouble().ToString("G", CultureInfo.InvariantCulture);
        }

        return cell.GetFormattedString().Trim();
    }

    private static DateTime? ReadDate(IXLRow row, int column)
    {
        var cell = row.Cell(column);

        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            return cell.GetDateTime();
        }

        var text = cell.GetFormattedString().Trim();

        if (DateTime.TryParseExact(
            text,
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var compactDate))
        {
            return compactDate;
        }

        if (DateTime.TryParse(
            text,
            CultureInfo.GetCultureInfo("el-GR"),
            DateTimeStyles.None,
            out var greek))
        {
            return greek;
        }

        if (DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var invariant))
        {
            return invariant;
        }

        return null;
    }

    private static string? ReadResultCode(IXLRow row, int? column)
    {
        if (column is null)
        {
            return null;
        }

        var cell = row.Cell(column.Value);
        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.DataType == XLDataType.Number)
        {
            var value = cell.GetDouble();
            if (value is >= 0 and <= 99 && Math.Abs(value - Math.Round(value)) < 0.0001)
            {
                return ((int)Math.Round(value)).ToString("00");
            }

            return value.ToString("G", CultureInfo.InvariantCulture);
        }

        var text = cell.GetFormattedString().Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed is >= 0 and <= 99)
        {
            return parsed.ToString("00");
        }

        return NullIfEmpty(text);
    }

    private static decimal? ReadDecimal(IXLRow row, int column)
    {
        var cell = row.Cell(column);
        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.DataType == XLDataType.Number)
        {
            return Convert.ToDecimal(cell.GetDouble(), CultureInfo.InvariantCulture);
        }

        var text = cell.GetFormattedString().Trim().Replace("€", string.Empty).Replace(" ", string.Empty);
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("el-GR"), out var greek))
        {
            return greek;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant))
        {
            return invariant;
        }

        return null;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Normalize(string value)
    {
        var folded = value.Trim().ToLowerInvariant()
            .Replace("ά", "α").Replace("έ", "ε").Replace("ή", "η").Replace("ί", "ι")
            .Replace("ό", "ο").Replace("ύ", "υ").Replace("ώ", "ω").Replace("ϊ", "ι").Replace("ϋ", "υ");

        var builder = new StringBuilder(folded.Length);
        foreach (var character in folded)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private sealed record ColumnMap(int Index, string Header, string Normalized);
}
