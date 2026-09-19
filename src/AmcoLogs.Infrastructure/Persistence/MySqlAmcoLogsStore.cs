using System.Text.Json;
using AmcoLogs.Core.Excel;
using AmcoLogs.Core.Persistence;
using MySqlConnector;

namespace AmcoLogs.Infrastructure.Persistence;

public sealed class MySqlAmcoLogsStore : IAmcoLogsStore
{
    private readonly LocalConnectionStore _connectionStore;
    private readonly object _gate = new();
    private MySqlConnectionSettings? _settings;

    public MySqlAmcoLogsStore(LocalConnectionStore connectionStore)
    {
        _connectionStore = connectionStore;
        _settings = connectionStore.Load();
    }

    public async Task<DatabaseStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = CurrentSettings();
        if (settings is null)
        {
            return new DatabaseStatus
            {
                Connected = false,
                Message = "No MySQL connection yet."
            };
        }

        try
        {
            await using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand("SELECT VERSION();", connection);
            var version = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
            return new DatabaseStatus
            {
                Connected = true,
                ServerVersion = version,
                Database = settings.Database,
                Host = settings.Host,
                User = settings.User,
                Message = "Database is ready."
            };
        }
        catch (Exception exception)
        {
            return new DatabaseStatus
            {
                Connected = false,
                Host = settings.Host,
                User = settings.User,
                Database = settings.Database,
                Message = exception.Message
            };
        }
    }

    public async Task<DatabaseStatus> ConnectAsync(MySqlConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        await using (var bootstrap = new MySqlConnection(BuildConnectionString(settings, includeDatabase: false)))
        {
            await bootstrap.OpenAsync(cancellationToken);
            await using var createDb = new MySqlCommand(
                $"CREATE DATABASE IF NOT EXISTS `{settings.Database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
                bootstrap);
            await createDb.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        lock (_gate)
        {
            _settings = settings;
        }

        _connectionStore.Save(settings);
        return await GetStatusAsync(cancellationToken);
    }

    public async Task<SavedExcelImport> SaveImportAsync(ExcelImportResult import, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        var batchId = Guid.NewGuid();
        var importedAt = DateTime.UtcNow;

        await using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var batch = new MySqlCommand(
            """
            INSERT INTO excel_import_batches (id, file_name, imported_at, row_count)
            VALUES (@id, @fileName, @importedAt, @rowCount);
            """, connection, transaction))
        {
            batch.Parameters.AddWithValue("@id", batchId.ToString());
            batch.Parameters.AddWithValue("@fileName", import.FileName);
            batch.Parameters.AddWithValue("@importedAt", importedAt);
            batch.Parameters.AddWithValue("@rowCount", import.Rows.Count);
            await batch.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var row in import.Rows)
        {
            await using var insert = new MySqlCommand(
                """
                INSERT INTO excel_import_rows (
                    id, batch_id, row_number, service_account_id, rrn, amount,
                    eticket_transaction_result, transaction_id, transaction_date,
                    mobility, extra_json)
                VALUES (
                    @id, @batchId, @rowNumber, @serviceAccountId, @rrn, @amount,
                    @result, @transactionId, @transactionDate,
                    @mobility, @extraJson);
                """, connection, transaction);

            insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("@batchId", batchId.ToString());
            insert.Parameters.AddWithValue("@rowNumber", row.RowNumber);
            insert.Parameters.AddWithValue("@serviceAccountId", (object?)row.Immediate.ServiceAccountId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@rrn", (object?)row.Immediate.Rrn ?? DBNull.Value);
            insert.Parameters.AddWithValue("@amount", (object?)row.Immediate.Amount ?? DBNull.Value);
            insert.Parameters.AddWithValue("@result", (object?)row.Immediate.EticketTransactionResult ?? DBNull.Value);
            insert.Parameters.AddWithValue("@transactionId", (object?)row.Immediate.TransactionId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@transactionDate", (object?)row.Immediate.TransactionDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("@mobility", row.Mobility.ToString());
            insert.Parameters.AddWithValue("@extraJson", JsonSerializer.Serialize(row.Archive.ExtraCodes));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new SavedExcelImport
        {
            BatchId = batchId,
            FileName = import.FileName,
            ImportedAt = importedAt,
            RowCount = import.Rows.Count
        };
    }

    public async Task<IReadOnlyList<SavedExcelImport>> ListRecentImportsAsync(int take = 20, CancellationToken cancellationToken = default)
    {
        var settings = CurrentSettings();
        if (settings is null)
        {
            return [];
        }

        await using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            """
            SELECT id, file_name, imported_at, row_count
            FROM excel_import_batches
            ORDER BY imported_at DESC
            LIMIT @take;
            """, connection);
        command.Parameters.AddWithValue("@take", take);

        var results = new List<SavedExcelImport>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SavedExcelImport
            {
                BatchId = Guid.Parse(reader.GetString("id")),
                FileName = reader.GetString("file_name"),
                ImportedAt = reader.GetDateTime("imported_at"),
                RowCount = reader.GetInt32("row_count")
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<DeviceIpMapping>> ListDeviceIpsAsync(CancellationToken cancellationToken = default)
    {
        var settings = CurrentSettings();
        if (settings is null)
        {
            return [];
        }

        try
        {
            await using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand(
                """
                SELECT service_account_id, ip_address, mobility, source, updated_at, bus_no
                FROM device_ip_mappings
                ORDER BY updated_at DESC;
                """, connection);

            var results = new List<DeviceIpMapping>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new DeviceIpMapping
                {
                    ServiceAccountId = reader.GetString("service_account_id"),
                    IpAddress = reader.GetString("ip_address"),
                    Mobility = reader.GetString("mobility"),
                    Source = reader.GetString("source"),
                    UpdatedAt = reader.GetDateTime("updated_at"),
                    BusNo = reader.IsDBNull(reader.GetOrdinal("bus_no")) ? null : reader.GetString("bus_no")
                });
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    public async Task UpsertDeviceIpAsync(DeviceIpMapping mapping, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        await using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            """
            INSERT INTO device_ip_mappings (service_account_id, ip_address, mobility, source, updated_at, bus_no)
            VALUES (@id, @ip, @mobility, @source, @updated, @bus)
            ON DUPLICATE KEY UPDATE
                ip_address = VALUES(ip_address),
                mobility = VALUES(mobility),
                source = VALUES(source),
                updated_at = VALUES(updated_at),
                bus_no = VALUES(bus_no);
            """, connection);
        command.Parameters.AddWithValue("@id", mapping.ServiceAccountId);
        command.Parameters.AddWithValue("@ip", mapping.IpAddress);
        command.Parameters.AddWithValue("@mobility", mapping.Mobility);
        command.Parameters.AddWithValue("@source", mapping.Source);
        command.Parameters.AddWithValue("@updated", mapping.UpdatedAt);
        command.Parameters.AddWithValue("@bus", (object?)mapping.BusNo ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredExcelRow>> ListAllExcelRowsAsync(int take = 2000, CancellationToken cancellationToken = default)
    {
        var settings = CurrentSettings();
        if (settings is null)
        {
            return [];
        }

        try
        {
            await using var connection = new MySqlConnection(BuildConnectionString(settings, includeDatabase: true));
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand(
                """
                SELECT r.batch_id, b.file_name, b.imported_at, r.row_number,
                       r.service_account_id, r.rrn, r.amount, r.eticket_transaction_result,
                       r.transaction_id, r.transaction_date, r.mobility, r.extra_json
                FROM excel_import_rows r
                INNER JOIN excel_import_batches b ON b.id = r.batch_id
                ORDER BY b.imported_at DESC, r.row_number
                LIMIT @take;
                """, connection);
            command.Parameters.AddWithValue("@take", take);

            var results = new List<StoredExcelRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new StoredExcelRow
                {
                    BatchId = Guid.Parse(reader.GetString("batch_id")),
                    FileName = reader.GetString("file_name"),
                    ImportedAt = reader.GetDateTime("imported_at"),
                    RowNumber = reader.GetInt32("row_number"),
                    ServiceAccountId = reader.IsDBNull(reader.GetOrdinal("service_account_id")) ? null : reader.GetString("service_account_id"),
                    Rrn = reader.IsDBNull(reader.GetOrdinal("rrn")) ? null : reader.GetString("rrn"),
                    Amount = reader.IsDBNull(reader.GetOrdinal("amount")) ? null : reader.GetDecimal("amount"),
                    EticketTransactionResult = reader.IsDBNull(reader.GetOrdinal("eticket_transaction_result")) ? null : reader.GetString("eticket_transaction_result"),
                    TransactionId = reader.IsDBNull(reader.GetOrdinal("transaction_id")) ? null : reader.GetString("transaction_id"),
                    TransactionDate = reader.IsDBNull(reader.GetOrdinal("transaction_date")) ? null : reader.GetDateTime("transaction_date"),
                    Mobility = reader.GetString("mobility"),
                    ExtraJson = reader.IsDBNull(reader.GetOrdinal("extra_json")) ? null : reader.GetString("extra_json")
                });
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    private static async Task EnsureSchemaAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        string[] statements =
        [
            """
            CREATE TABLE IF NOT EXISTS excel_import_batches (
                id CHAR(36) NOT NULL PRIMARY KEY,
                file_name VARCHAR(255) NOT NULL,
                imported_at DATETIME(6) NOT NULL,
                row_count INT NOT NULL
            )
            """,
            """
            CREATE TABLE IF NOT EXISTS excel_import_rows (
                id CHAR(36) NOT NULL PRIMARY KEY,
                batch_id CHAR(36) NOT NULL,
                row_number INT NOT NULL,
                service_account_id VARCHAR(64) NULL,
                rrn VARCHAR(64) NULL,
                amount DECIMAL(12,4) NULL,
                eticket_transaction_result VARCHAR(128) NULL,
                transaction_id VARCHAR(64) NULL,
                transaction_date DATETIME(6) NULL,
                mobility VARCHAR(16) NOT NULL,
                extra_json JSON NULL,
                CONSTRAINT fk_excel_rows_batch
                    FOREIGN KEY (batch_id) REFERENCES excel_import_batches(id)
                    ON DELETE CASCADE
            )
            """,
            """
            CREATE TABLE IF NOT EXISTS device_ip_mappings (
                service_account_id VARCHAR(64) NOT NULL PRIMARY KEY,
                ip_address VARCHAR(64) NOT NULL,
                mobility VARCHAR(16) NOT NULL,
                source VARCHAR(32) NOT NULL DEFAULT 'thingsboard',
                updated_at DATETIME(6) NOT NULL,
                bus_no VARCHAR(64) NULL
            )
            """
        ];

        foreach (var sql in statements)
        {
            await using var command = new MySqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await MigrateLegacyColumnsAsync(connection, cancellationToken);
    }

    private static async Task MigrateLegacyColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(connection, "excel_import_rows", "ctvm_code", cancellationToken)
            && !await ColumnExistsAsync(connection, "excel_import_rows", "service_account_id", cancellationToken))
        {
            await ExecuteAsync(connection, "ALTER TABLE excel_import_rows CHANGE COLUMN ctvm_code service_account_id VARCHAR(64) NULL;", cancellationToken);
        }

        if (await ColumnExistsAsync(connection, "excel_import_rows", "ticket_value", cancellationToken)
            && !await ColumnExistsAsync(connection, "excel_import_rows", "amount", cancellationToken))
        {
            await ExecuteAsync(connection, "ALTER TABLE excel_import_rows CHANGE COLUMN ticket_value amount DECIMAL(12,4) NULL;", cancellationToken);
        }

        if (await ColumnExistsAsync(connection, "excel_import_rows", "occurred_at", cancellationToken)
            && !await ColumnExistsAsync(connection, "excel_import_rows", "transaction_date", cancellationToken))
        {
            await ExecuteAsync(connection, "ALTER TABLE excel_import_rows CHANGE COLUMN occurred_at transaction_date DATETIME(6) NULL;", cancellationToken);
        }

        if (!await ColumnExistsAsync(connection, "excel_import_rows", "eticket_transaction_result", cancellationToken))
        {
            await ExecuteAsync(connection, "ALTER TABLE excel_import_rows ADD COLUMN eticket_transaction_result VARCHAR(128) NULL AFTER amount;", cancellationToken);
        }

        if (await ColumnExistsAsync(connection, "excel_import_rows", "terminal_number", cancellationToken))
        {
            await ExecuteAsync(connection, "ALTER TABLE excel_import_rows DROP COLUMN terminal_number;", cancellationToken);
        }

        if (await ColumnExistsAsync(connection, "device_ip_mappings", "ctvm_code", cancellationToken)
            && !await ColumnExistsAsync(connection, "device_ip_mappings", "service_account_id", cancellationToken))
        {
            await ExecuteAsync(connection, "ALTER TABLE device_ip_mappings CHANGE COLUMN ctvm_code service_account_id VARCHAR(64) NOT NULL;", cancellationToken);
        }

        if (!await ColumnExistsAsync(connection, "device_ip_mappings", "bus_no", cancellationToken))
        {
            await ExecuteAsync(connection, "ALTER TABLE device_ip_mappings ADD COLUMN bus_no VARCHAR(64) NULL;", cancellationToken);
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        MySqlConnection connection,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @table
              AND COLUMN_NAME = @column;
            """, connection);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return count > 0;
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private MySqlConnectionSettings? CurrentSettings()
    {
        lock (_gate)
        {
            return _settings;
        }
    }

    private MySqlConnectionSettings RequireSettings() =>
        CurrentSettings() ?? throw new InvalidOperationException("Δεν υπάρχει ενεργή σύνδεση MySQL.");

    private static string BuildConnectionString(MySqlConnectionSettings settings, bool includeDatabase)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.Host,
            Port = (uint)settings.Port,
            UserID = settings.User,
            Password = settings.Password,
            CharacterSet = "utf8mb4",
            SslMode = MySqlSslMode.Preferred,
            AllowPublicKeyRetrieval = true,
            AllowLoadLocalInfile = false
        };

        if (includeDatabase)
        {
            builder.Database = settings.Database;
        }

        return builder.ConnectionString;
    }
}
