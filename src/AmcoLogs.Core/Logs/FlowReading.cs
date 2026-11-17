namespace AmcoLogs.Core.Logs;

public sealed class FlowDeviation
{
    public int StepNumber { get; init; }
    public string Expected { get; init; } = "";
}

public sealed class ErrorSampleReading
{
    public string ErrorKind { get; init; } = "";
    public int SampleCount { get; init; }
    public int MatchedCount { get; init; }
}

public sealed class FlowReading
{
    public bool HasArchetype { get; init; }
    public int StepCount { get; init; }
    public int MatchedSteps { get; init; }
    public string? ErrorKind { get; init; }
    public string FlowMessage { get; init; } = "";
    public string ErrorMessage { get; init; } = "";
    public IReadOnlyList<FlowDeviation> Deviations { get; init; } = [];
    public IReadOnlyList<ErrorSampleReading> Readings { get; init; } = [];
}

public interface ITransactionFlowReader
{
    FlowReading Read(IReadOnlyList<string> transactionLines, string? profile);
}
