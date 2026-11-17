using MySqlConnector;

namespace AmcoLogs.Infrastructure.Persistence;

public static class DeskTables
{
    public static readonly string[] Scopes = ["oseth", "oasa_tram", "oasa_metro"];

    public static readonly (string Code, string Label, int Sort)[] ErrorKinds =
    [
        ("restart", "Restart", 1),
        ("printer", "Not Operational Printer", 2),
        ("database_error", "Database error", 3),
        ("customer_mistake", "Mistake of a customer", 4),
        ("card_reader", "Card reader", 5),
        ("network", "Network", 6),
        ("power", "Power", 7),
        ("amount_mismatch", "Amount mismatch", 8),
        ("other", "Other", 9)
    ];

    public static IEnumerable<string> CreateStatements()
    {
        foreach (var scope in Scopes)
        {
            yield return $"""
                CREATE TABLE IF NOT EXISTS desk_case_{scope} (
                    id CHAR(36) NOT NULL PRIMARY KEY,
                    origin VARCHAR(16) NOT NULL,
                    file_name VARCHAR(255) NULL,
                    file_date DATE NULL,
                    imported_at DATETIME(6) NOT NULL,
                    service_account_id VARCHAR(64) NULL,
                    fault_type VARCHAR(128) NULL,
                    code_02_count INT NOT NULL DEFAULT 0,
                    code_03_count INT NOT NULL DEFAULT 0,
                    yes_count INT NOT NULL DEFAULT 0,
                    no_count INT NOT NULL DEFAULT 0,
                    logs_downloaded TINYINT NOT NULL DEFAULT 0,
                    investigation_status VARCHAR(32) NOT NULL DEFAULT 'open',
                    conclusion VARCHAR(8) NULL
                )
                """;

            yield return $"""
                CREATE TABLE IF NOT EXISTS desk_case_line_{scope} (
                    id CHAR(36) NOT NULL PRIMARY KEY,
                    case_id CHAR(36) NOT NULL,
                    row_number INT NOT NULL,
                    service_account_id VARCHAR(64) NULL,
                    fault_type VARCHAR(128) NULL,
                    result_code VARCHAR(8) NULL,
                    decision VARCHAR(8) NULL,
                    logs_downloaded TINYINT NOT NULL DEFAULT 0,
                    investigation_status VARCHAR(32) NOT NULL DEFAULT 'open',
                    conclusion VARCHAR(8) NULL,
                    transaction_id VARCHAR(64) NULL,
                    transaction_date DATETIME(6) NULL,
                    amount DECIMAL(12,4) NULL,
                    terminal_code VARCHAR(64) NULL,
                    station_name VARCHAR(128) NULL,
                    CONSTRAINT fk_desk_line_{scope}
                        FOREIGN KEY (case_id) REFERENCES desk_case_{scope}(id)
                        ON DELETE CASCADE
                )
                """;

            yield return $"""
                CREATE TABLE IF NOT EXISTS desk_ip_{scope} (
                    service_account_id VARCHAR(64) NOT NULL PRIMARY KEY,
                    ip_address VARCHAR(64) NOT NULL,
                    mobility VARCHAR(16) NOT NULL,
                    source VARCHAR(32) NOT NULL,
                    bus_no VARCHAR(64) NULL,
                    terminal_code VARCHAR(64) NULL,
                    station_name VARCHAR(128) NULL,
                    updated_at DATETIME(6) NOT NULL
                )
                """;

            yield return $"""
                CREATE TABLE IF NOT EXISTS desk_error_entry_{scope} (
                    id CHAR(36) NOT NULL PRIMARY KEY,
                    service_account_id VARCHAR(64) NOT NULL,
                    error_kind VARCHAR(32) NOT NULL,
                    occurred_on DATE NOT NULL,
                    created_at DATETIME(6) NOT NULL,
                    KEY idx_error_device_{scope} (service_account_id),
                    KEY idx_error_kind_{scope} (error_kind)
                )
                """;

            yield return $"""
                CREATE TABLE IF NOT EXISTS desk_error_kind_{scope} (
                    code VARCHAR(32) NOT NULL PRIMARY KEY,
                    label VARCHAR(128) NOT NULL,
                    sort_order INT NOT NULL
                )
                """;
        }
    }

    public static IEnumerable<string> SeedStatements()
    {
        foreach (var scope in Scopes)
        {
            var values = string.Join(",\n", ErrorKinds.Select(kind => $"('{kind.Code}', '{kind.Label}', {kind.Sort})"));
            yield return $"""
                INSERT IGNORE INTO desk_error_kind_{scope} (code, label, sort_order) VALUES
                {values};
                """;
        }
    }

    public static async Task ApplyAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        foreach (var sql in CreateStatements().Concat(AlterStatements()).Concat(SeedStatements()))
        {
            try
            {
                await using var command = new MySqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (MySqlException exception) when (exception.Number == 1060)
            {
            }
        }
    }

    private static IEnumerable<string> AlterStatements()
    {
        foreach (var scope in Scopes)
        {
            yield return $"ALTER TABLE desk_case_line_{scope} ADD COLUMN terminal_code VARCHAR(64) NULL";
            yield return $"ALTER TABLE desk_case_line_{scope} ADD COLUMN station_name VARCHAR(128) NULL";
            yield return $"ALTER TABLE desk_ip_{scope} ADD COLUMN terminal_code VARCHAR(64) NULL";
            yield return $"ALTER TABLE desk_ip_{scope} ADD COLUMN station_name VARCHAR(128) NULL";
        }
    }
}
