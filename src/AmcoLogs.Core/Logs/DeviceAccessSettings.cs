namespace AmcoLogs.Core.Logs;

public sealed class DeviceAccessSettings
{
    public string? Username { get; set; } = "amco";
    public string? Password { get; set; }
    public int Port { get; set; } = 8022;
    public string RemoteLogPath { get; set; } = "/opt/amco/middleware/TVMserver";
    public string? LocalDownloadFolder { get; set; }
}
