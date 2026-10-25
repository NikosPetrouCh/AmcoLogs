namespace AmcoLogs.Core.Logs;

public sealed class LogSliceLine
{
    public int LineNumber { get; init; }
    public required string Text { get; init; }
    public required string Role { get; init; }
}

public sealed class LogSliceResult
{
    public bool Found { get; init; }
    public required string Message { get; init; }
    public string? LogFileName { get; init; }
    public int? LineStart { get; init; }
    public int? LineEnd { get; init; }
    public IReadOnlyList<LogSliceLine> Lines { get; init; } = [];
}
