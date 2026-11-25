using AmcoLogs.Core.Excel;
using AmcoLogs.Core.Persistence;
using MySqlConnector;

namespace AmcoLogs.Infrastructure.Persistence;

public sealed class MySqlDeskArchive
{
    private readonly LocalConnectionStore _connections;

    public MySqlDeskArchive(LocalConnectionStore connections)
    {
        _connections = connections;
    }

    public async Task<DeskCaseSummary> SaveCaseAsync(DeskCaseDraft draft, CancellationToken cancellationToken = default)
    {
        var scope = DeskScope.TableKey(draft.Operator, draft.Network);
        var lines = draft.Lines ?? [];
        var code02 = lines.Count(line => ResultCode(line.ResultCode) == "02");
        var code03 = lines.Count(line => ResultCode(line.ResultCode) == "03");
        var yes = lines.Count(line => string.Equals(line.Decision, "YES", StringComparison.OrdinalIgnoreCase));
        var no = lines.Count(line => string.Equals(line.Decision, "NO", StringComparison.OrdinalIgnoreCase));
        var id = Guid.NewGuid();
        var importedAt = DateTime.UtcNow;

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new MySqlCommand(
            $"""
            INSERT INTO desk_case_{scope} (
                id, origin, file_name, file_date, imported_at, service_account_id, fault_type,
                code_02_count, code_03_count, yes_count, no_count, logs_downloaded, investigation_status, conclusion)
            VALUES (
                @id, @origin, @fileName, @fileDate, @importedAt, @serviceAccountId, @faultType,
                @code02, @code03, @yes, @no, 0, 'open', NULL);
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("@id", id.ToString());
            insert.Parameters.AddWithValue("@origin", draft.Origin == "device" ? "device" : "excel");
            insert.Parameters.AddWithValue("@fileName", (object?)draft.FileName ?? DBNull.Value);
            insert.Parameters.AddWithValue("@fileDate", draft.FileDate.HasValue ? draft.FileDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
            insert.Parameters.AddWithValue("@importedAt", importedAt);
            insert.Parameters.AddWithValue("@serviceAccountId", (object?)draft.ServiceAccountId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@faultType", (object?)NullIfEmpty(draft.FaultType) ?? DBNull.Value);
            insert.Parameters.AddWithValue("@code02", code02);
            insert.Parameters.AddWithValue("@code03", code03);
            insert.Parameters.AddWithValue("@yes", yes);
            insert.Parameters.AddWithValue("@no", no);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var rowNumber = 1;
        foreach (var line in lines)
        {
            await using var lineCommand = new MySqlCommand(
                $"""
                INSERT INTO desk_case_line_{scope} (
                id, case_id, `row_number`, service_account_id, fault_type, result_code, decision,
                    logs_downloaded, investigation_status, conclusion, transaction_id, transaction_date, amount,
                    terminal_code, station_name)
                VALUES (
                    @id, @caseId, @rowNumber, @serviceAccountId, @faultType, @resultCode, @decision,
                    0, 'open', @conclusion, @transactionId, @transactionDate, @amount,
                    @terminal, @station);
                """, connection, transaction);
            lineCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            lineCommand.Parameters.AddWithValue("@caseId", id.ToString());
            lineCommand.Parameters.AddWithValue("@rowNumber", line.RowNumber > 0 ? line.RowNumber : rowNumber);
            lineCommand.Parameters.AddWithValue("@serviceAccountId", (object?)line.ServiceAccountId ?? DBNull.Value);
            lineCommand.Parameters.AddWithValue("@faultType", (object?)NullIfEmpty(line.FaultType) ?? DBNull.Value);
            lineCommand.Parameters.AddWithValue("@resultCode", (object?)ResultCode(line.ResultCode) ?? DBNull.Value);
            lineCommand.Parameters.AddWithValue("@decision", (object?)NormalizeDecision(line.Decision) ?? DBNull.Value);
            lineCommand.Parameters.AddWithValue("@conclusion", (object?)NormalizeDecision(line.Decision) ?? DBNull.Value);
            lineCommand.Parameters.AddWithValue("@transactionId", (object?)line.TransactionId ?? DBNull.Value);
            lineCommand.Parameters.AddWithValue("@transactionDate", (object?)line.TransactionDate ?? DBNull.Value);
            lineCommand.Parameters.AddWithValue("@amount", (object?)line.Amount ?? DBNull.Value);
            lineCommand.Parameters.AddWithValue("@terminal", (object?)NullIfEmpty(line.TerminalCode) ?? DBNull.Value);
            lineCommand.Parameters.AddWithValue("@station", (object?)NullIfEmpty(line.StationName) ?? DBNull.Value);
            await lineCommand.ExecuteNonQueryAsync(cancellationToken);
            rowNumber++;
        }

        await transaction.CommitAsync(cancellationToken);
        return new DeskCaseSummary
        {
            Id = id,
            Origin = draft.Origin == "device" ? "device" : "excel",
            FileName = draft.FileName,
            FileDate = draft.FileDate,
            ImportedAt = importedAt,
            ServiceAccountId = draft.ServiceAccountId,
            FaultType = NullIfEmpty(draft.FaultType),
            Code02Count = code02,
            Code03Count = code03,
            YesCount = yes,
            NoCount = no,
            InvestigationStatus = "open"
        };
    }

    public async Task SaveExcelCaseAsync(string operatorName, string? network, ExcelImportResult import, CancellationToken cancellationToken = default)
    {
        var lines = import.Rows
            .Where(row => row.Disposition is RowDisposition.AutomaticYes or RowDisposition.NeedsIp)
            .Select(row => new DeskLineDraft
            {
                RowNumber = row.RowNumber,
                ServiceAccountId = row.Immediate.ServiceAccountId,
                ResultCode = row.Immediate.EticketTransactionResult,
                Decision = row.Decision,
                TransactionId = row.Immediate.TransactionId,
                TransactionDate = row.Immediate.TransactionDate,
                Amount = row.Immediate.Amount,
                FaultType = row.StatusCode,
                TerminalCode = row.OasaTerminal,
                StationName = row.StationName
            })
            .ToList();
        if (lines.Count == 0)
        {
            return;
        }

        var title = ExcelBatchTitle.Parse(import.FileName);
        var dates = lines
            .Where(line => line.TransactionDate.HasValue)
            .Select(line => DateOnly.FromDateTime(line.TransactionDate!.Value))
            .ToList();
        await SaveCaseAsync(new DeskCaseDraft
        {
            Operator = operatorName,
            Network = network,
            Origin = "excel",
            FileName = import.FileName,
            FileDate = title.FileDate ?? (dates.Count > 0 ? dates.Min() : null),
            Lines = lines
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<DeskCaseSummary>> ListCasesAsync(string? operatorName, string? network, CancellationToken cancellationToken = default)
    {
        var scope = DeskScope.TableKey(operatorName, network);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"""
            SELECT id, origin, file_name, file_date, imported_at, service_account_id, fault_type,
                   code_02_count, code_03_count, yes_count, no_count, logs_downloaded, investigation_status, conclusion
            FROM desk_case_{scope}
            ORDER BY file_date DESC, imported_at DESC;
            """, connection);
        var results = new List<DeskCaseSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadCase(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<DeskCaseLine>> ListLinesAsync(string? operatorName, string? network, Guid? caseId, CancellationToken cancellationToken = default)
    {
        var scope = DeskScope.TableKey(operatorName, network);
        await using var connection = await OpenAsync(cancellationToken);
        var sql = $"""
            SELECT l.id, l.case_id, l.`row_number`, l.service_account_id, l.fault_type, l.result_code, l.decision,
                   l.logs_downloaded, l.investigation_status, l.conclusion, l.transaction_id, l.transaction_date, l.amount,
                   l.terminal_code, l.station_name,
                   c.file_name, c.file_date, c.origin
            FROM desk_case_line_{scope} l
            INNER JOIN desk_case_{scope} c ON c.id = l.case_id
            """;
        if (caseId.HasValue)
        {
            sql += " WHERE l.case_id = @caseId";
        }

        sql += " ORDER BY c.file_date DESC, l.`row_number`;";
        await using var command = new MySqlCommand(sql, connection);
        if (caseId.HasValue)
        {
            command.Parameters.AddWithValue("@caseId", caseId.Value.ToString());
        }

        var results = new List<DeskCaseLine>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadLine(reader));
        }

        return results;
    }

    public async Task PatchCaseAsync(Guid id, DeskCasePatch patch, CancellationToken cancellationToken = default)
    {
        var scope = DeskScope.TableKey(patch.Operator, patch.Network);
        var sets = new List<string>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand { Connection = connection };
        if (patch.HasFileDate)
        {
            sets.Add("file_date = @fileDate");
            command.Parameters.AddWithValue("@fileDate", patch.FileDate.HasValue ? patch.FileDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        }

        if (patch.HasFaultType)
        {
            sets.Add("fault_type = @faultType");
            command.Parameters.AddWithValue("@faultType", (object?)NullIfEmpty(patch.FaultType) ?? DBNull.Value);
        }

        if (patch.HasConclusion)
        {
            sets.Add("conclusion = @conclusion");
            command.Parameters.AddWithValue("@conclusion", (object?)NormalizeDecision(patch.Conclusion) ?? DBNull.Value);
        }

        if (patch.HasInvestigationStatus)
        {
            sets.Add("investigation_status = @status");
            command.Parameters.AddWithValue("@status", NormalizeStatus(patch.InvestigationStatus));
        }

        if (sets.Count == 0)
        {
            return;
        }

        command.CommandText = $"UPDATE desk_case_{scope} SET {string.Join(", ", sets)} WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PatchLineAsync(Guid id, DeskLinePatch patch, CancellationToken cancellationToken = default)
    {
        var scope = DeskScope.TableKey(patch.Operator, patch.Network);
        var sets = new List<string>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand { Connection = connection };
        if (patch.LogsDownloaded.HasValue)
        {
            sets.Add("logs_downloaded = @logs");
            command.Parameters.AddWithValue("@logs", patch.LogsDownloaded.Value ? 1 : 0);
        }

        if (patch.HasFaultType)
        {
            sets.Add("fault_type = @faultType");
            command.Parameters.AddWithValue("@faultType", (object?)NullIfEmpty(patch.FaultType) ?? DBNull.Value);
        }

        if (patch.HasConclusion)
        {
            sets.Add("conclusion = @conclusion");
            command.Parameters.AddWithValue("@conclusion", (object?)NormalizeDecision(patch.Conclusion) ?? DBNull.Value);
        }

        if (patch.HasInvestigationStatus)
        {
            sets.Add("investigation_status = @status");
            command.Parameters.AddWithValue("@status", NormalizeStatus(patch.InvestigationStatus));
        }

        if (sets.Count == 0)
        {
            return;
        }

        command.CommandText = $"UPDATE desk_case_line_{scope} SET {string.Join(", ", sets)} WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString());
        var updated = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updated > 0 && patch.LogsDownloaded == true)
        {
            await using var mark = new MySqlCommand(
                $"""
                UPDATE desk_case_{scope} c
                SET logs_downloaded = 1, investigation_status = IF(c.investigation_status = 'open', 'logs', c.investigation_status)
                WHERE c.id = (SELECT case_id FROM desk_case_line_{scope} WHERE id = @id);
                """, connection);
            mark.Parameters.AddWithValue("@id", id.ToString());
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<DeskIpRow>> ListIpsAsync(string? operatorName, string? network, CancellationToken cancellationToken = default)
    {
        var scope = DeskScope.TableKey(operatorName, network);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"SELECT service_account_id, ip_address, mobility, source, bus_no, terminal_code, station_name, updated_at FROM desk_ip_{scope} ORDER BY service_account_id;",
            connection);
        var results = new List<DeskIpRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DeskIpRow
            {
                ServiceAccountId = reader.GetString("service_account_id"),
                IpAddress = reader.GetString("ip_address"),
                Mobility = reader.GetString("mobility"),
                Source = reader.GetString("source"),
                BusNo = reader.IsDBNull(reader.GetOrdinal("bus_no")) ? null : reader.GetString("bus_no"),
                TerminalCode = reader.IsDBNull(reader.GetOrdinal("terminal_code")) ? null : reader.GetString("terminal_code"),
                StationName = reader.IsDBNull(reader.GetOrdinal("station_name")) ? null : reader.GetString("station_name"),
                UpdatedAt = reader.GetDateTime("updated_at"),
                Operator = operatorName ?? "OSETH",
                Network = network
            });
        }

        return results;
    }

    public async Task UpsertIpAsync(DeskIpRow row, CancellationToken cancellationToken = default)
    {
        var scope = DeskScope.TableKey(row.Operator, row.Network);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"""
            INSERT INTO desk_ip_{scope} (
                service_account_id, ip_address, mobility, source, bus_no, terminal_code, station_name, updated_at)
            VALUES (@id, @ip, @mobility, @source, @bus, @terminal, @station, @updated)
            ON DUPLICATE KEY UPDATE
                ip_address = VALUES(ip_address),
                mobility = VALUES(mobility),
                source = VALUES(source),
                bus_no = VALUES(bus_no),
                terminal_code = VALUES(terminal_code),
                station_name = VALUES(station_name),
                updated_at = VALUES(updated_at);
            """, connection);
        command.Parameters.AddWithValue("@id", row.ServiceAccountId.Trim());
        command.Parameters.AddWithValue("@ip", row.IpAddress.Trim());
        command.Parameters.AddWithValue("@mobility", string.IsNullOrWhiteSpace(row.Mobility) ? "Unknown" : row.Mobility);
        command.Parameters.AddWithValue("@source", string.IsNullOrWhiteSpace(row.Source) ? "manual" : row.Source);
        command.Parameters.AddWithValue("@bus", (object?)NullIfEmpty(row.BusNo) ?? DBNull.Value);
        command.Parameters.AddWithValue("@terminal", (object?)NullIfEmpty(row.TerminalCode) ?? DBNull.Value);
        command.Parameters.AddWithValue("@station", (object?)NullIfEmpty(row.StationName) ?? DBNull.Value);
        command.Parameters.AddWithValue("@updated", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var place = new MySqlCommand(
            $"""
            UPDATE desk_case_line_{scope}
            SET terminal_code = @terminal, station_name = @station
            WHERE service_account_id = @id;
            """, connection);
        place.Parameters.AddWithValue("@id", row.ServiceAccountId.Trim());
        place.Parameters.AddWithValue("@terminal", (object?)NullIfEmpty(row.TerminalCode) ?? DBNull.Value);
        place.Parameters.AddWithValue("@station", (object?)NullIfEmpty(row.StationName) ?? DBNull.Value);
        await place.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeskErrorEntry>> ListErrorsAsync(string? operatorName, string? network, CancellationToken cancellationToken = default)
    {
        var scope = DeskScope.TableKey(operatorName, network);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"""
            SELECT id, service_account_id, error_kind, occurred_on, created_at
            FROM desk_error_entry_{scope}
            ORDER BY occurred_on DESC, created_at DESC;
            """, connection);
        var results = new List<DeskErrorEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadError(reader, operatorName, network));
        }

        return results;
    }

    public async Task<DeskErrorEntry> AddErrorAsync(DeskErrorEntry entry, CancellationToken cancellationToken = default)
    {
        var scope = DeskScope.TableKey(entry.Operator, entry.Network);
        var kind = DeskTables.ErrorKinds.FirstOrDefault(item => string.Equals(item.Code, entry.ErrorKind, StringComparison.OrdinalIgnoreCase));
        if (kind.Code is null)
        {
            throw new InvalidOperationException("Διάλεξε τύπο σφάλματος.");
        }

        if (entry.OccurredOn == default)
        {
            throw new InvalidOperationException("Χρειάζεται ημερομηνία.");
        }

        entry.Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;
        entry.ServiceAccountId = entry.ServiceAccountId.Trim();
        entry.ErrorKind = kind.Code;
        entry.CreatedAt = DateTime.UtcNow;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"""
            INSERT INTO desk_error_entry_{scope} (
                id, service_account_id, error_kind, occurred_on, created_at)
            VALUES (
                @id, @device, @kind, @occurred, @created);
            """, connection);
        command.Parameters.AddWithValue("@id", entry.Id.ToString());
        command.Parameters.AddWithValue("@device", entry.ServiceAccountId);
        command.Parameters.AddWithValue("@kind", entry.ErrorKind);
        command.Parameters.AddWithValue("@occurred", entry.OccurredOn.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@created", entry.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return entry;
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var settings = _connections.Load() ?? throw new InvalidOperationException("Δεν υπάρχει ενεργή σύνδεση MySQL.");
        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.Host,
            Port = (uint)settings.Port,
            UserID = settings.User,
            Password = settings.Password,
            Database = settings.Database,
            ConnectionTimeout = 5
        };
        var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (MySqlException)
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException("Η MySQL δεν είναι συνδεδεμένη.");
        }
        await DeskTables.ApplyAsync(connection, cancellationToken);

        return connection;
    }

    private static DeskCaseSummary ReadCase(MySqlDataReader reader)
    {
        var dateOrdinal = reader.GetOrdinal("file_date");
        return new DeskCaseSummary
        {
            Id = reader.GetGuid("id"),
            Origin = reader.GetString("origin"),
            FileName = reader.IsDBNull(reader.GetOrdinal("file_name")) ? null : reader.GetString("file_name"),
            FileDate = reader.IsDBNull(dateOrdinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(dateOrdinal)),
            ImportedAt = reader.GetDateTime("imported_at"),
            ServiceAccountId = reader.IsDBNull(reader.GetOrdinal("service_account_id")) ? null : reader.GetString("service_account_id"),
            FaultType = reader.IsDBNull(reader.GetOrdinal("fault_type")) ? null : reader.GetString("fault_type"),
            Code02Count = reader.GetInt32("code_02_count"),
            Code03Count = reader.GetInt32("code_03_count"),
            YesCount = reader.GetInt32("yes_count"),
            NoCount = reader.GetInt32("no_count"),
            LogsDownloaded = reader.GetInt32("logs_downloaded") != 0,
            InvestigationStatus = reader.GetString("investigation_status"),
            Conclusion = reader.IsDBNull(reader.GetOrdinal("conclusion")) ? null : reader.GetString("conclusion")
        };
    }

    private static DeskCaseLine ReadLine(MySqlDataReader reader)
    {
        var dateOrdinal = reader.GetOrdinal("file_date");
        var txOrdinal = reader.GetOrdinal("transaction_date");
        return new DeskCaseLine
        {
            Id = Guid.Parse(reader.GetString("id")),
            CaseId = Guid.Parse(reader.GetString("case_id")),
            RowNumber = reader.GetInt32("row_number"),
            ServiceAccountId = reader.IsDBNull(reader.GetOrdinal("service_account_id")) ? null : reader.GetString("service_account_id"),
            FaultType = reader.IsDBNull(reader.GetOrdinal("fault_type")) ? null : reader.GetString("fault_type"),
            ResultCode = reader.IsDBNull(reader.GetOrdinal("result_code")) ? null : reader.GetString("result_code"),
            Decision = reader.IsDBNull(reader.GetOrdinal("decision")) ? null : reader.GetString("decision"),
            LogsDownloaded = reader.GetInt32("logs_downloaded") != 0,
            InvestigationStatus = reader.GetString("investigation_status"),
            Conclusion = reader.IsDBNull(reader.GetOrdinal("conclusion")) ? null : reader.GetString("conclusion"),
            TransactionId = reader.IsDBNull(reader.GetOrdinal("transaction_id")) ? null : reader.GetString("transaction_id"),
            TransactionDate = reader.IsDBNull(txOrdinal) ? null : reader.GetDateTime(txOrdinal),
            Amount = reader.IsDBNull(reader.GetOrdinal("amount")) ? null : reader.GetDecimal("amount"),
            TerminalCode = reader.IsDBNull(reader.GetOrdinal("terminal_code")) ? null : reader.GetString("terminal_code"),
            StationName = reader.IsDBNull(reader.GetOrdinal("station_name")) ? null : reader.GetString("station_name"),
            FileName = reader.IsDBNull(reader.GetOrdinal("file_name")) ? null : reader.GetString("file_name"),
            FileDate = reader.IsDBNull(dateOrdinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(dateOrdinal)),
            Origin = reader.GetString("origin")
        };
    }

    private static DeskErrorEntry ReadError(MySqlDataReader reader, string? operatorName, string? network)
    {
        var occurred = reader.GetDateTime("occurred_on");
        return new DeskErrorEntry
        {
            Id = Guid.Parse(reader.GetString("id")),
            ServiceAccountId = reader.GetString("service_account_id"),
            ErrorKind = reader.GetString("error_kind"),
            OccurredOn = DateOnly.FromDateTime(occurred),
            CreatedAt = reader.GetDateTime("created_at"),
            Operator = operatorName ?? "OSETH",
            Network = network
        };
    }

    private static string? ResultCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        return int.TryParse(text, out var number) && number is >= 0 and <= 99
            ? number.ToString("00")
            : text;
    }

    private static string? NormalizeDecision(string? value)
    {
        if (string.Equals(value, "YES", StringComparison.OrdinalIgnoreCase))
        {
            return "YES";
        }

        if (string.Equals(value, "NO", StringComparison.OrdinalIgnoreCase))
        {
            return "NO";
        }

        return null;
    }

    private static string NormalizeStatus(string? value)
    {
        if (string.Equals(value, "logs", StringComparison.OrdinalIgnoreCase))
        {
            return "logs";
        }

        if (string.Equals(value, "concluded", StringComparison.OrdinalIgnoreCase))
        {
            return "concluded";
        }

        return "open";
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
