namespace AmcoLogs.Core.Excel;

public sealed class ExcelImportResult
{
    public required string FileName { get; init; }
    public IReadOnlyList<ExcelImportRow> Rows { get; init; } = [];
    public IReadOnlyList<string> RecognizedHeaders { get; init; } = [];
    public IReadOnlyList<string> ArchiveExtraHeaders { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
