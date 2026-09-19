namespace AmcoLogs.Core.Persistence;

public sealed class DatabaseStatus
{
    public bool Connected { get; init; }
    public string? ServerVersion { get; init; }
    public string? Database { get; init; }
    public string? Host { get; init; }
    public string? User { get; init; }
    public string? Message { get; init; }
}
