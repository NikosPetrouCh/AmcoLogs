namespace AmcoLogs.Core.Persistence;

public sealed class MySqlConnectionSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string User { get; set; } = "root";
    public string Password { get; set; } = string.Empty;
    public string Database { get; set; } = "amco_logs";

    public MySqlConnectionSettings WithoutPassword() => new()
    {
        Host = Host,
        Port = Port,
        User = User,
        Database = Database
    };
}
