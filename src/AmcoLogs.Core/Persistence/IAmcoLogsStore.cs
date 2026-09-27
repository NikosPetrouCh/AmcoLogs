using AmcoLogs.Core.Excel;
using AmcoLogs.Core.Logs;

namespace AmcoLogs.Core.Persistence;

public interface IAmcoLogsStore
{
    Task<DatabaseStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<DatabaseStatus> ConnectAsync(MySqlConnectionSettings settings, CancellationToken cancellationToken = default);
    Task<SavedExcelImport> SaveImportAsync(ExcelImportResult import, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedExcelImport>> ListRecentImportsAsync(int take = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeviceIpMapping>> ListDeviceIpsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredExcelRow>> ListAllExcelRowsAsync(int take = 2000, CancellationToken cancellationToken = default);
    Task UpsertDeviceIpAsync(DeviceIpMapping mapping, CancellationToken cancellationToken = default);
    Task SaveFindingsAsync(IReadOnlyList<InvestigationFinding> findings, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvestigationFinding>> ListFindingsAsync(int take = 500, CancellationToken cancellationToken = default);
}
