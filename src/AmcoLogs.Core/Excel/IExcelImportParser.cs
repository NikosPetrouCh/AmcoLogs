namespace AmcoLogs.Core.Excel;

public interface IExcelImportParser
{
    ExcelImportResult Parse(Stream stream, string fileName);
}
