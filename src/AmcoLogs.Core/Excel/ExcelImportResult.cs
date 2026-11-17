namespace AmcoLogs.Core.Excel;

public sealed class ExcelImportResult
{
    public const string OsethProfile = "OSETH";
    public const string OasaProfile = "OASA";

    public required string FileName { get; init; }
    public string Profile { get; init; } = OsethProfile;
    public IReadOnlyList<ExcelImportRow> Rows { get; init; } = [];
    public IReadOnlyList<string> RecognizedHeaders { get; init; } = [];
    public IReadOnlyList<string> ArchiveExtraHeaders { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
