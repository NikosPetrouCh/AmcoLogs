namespace AmcoLogs.Core.Excel;

/// <summary>
/// Any extra Excel columns that are not in the immediate-use set.
/// </summary>
public sealed class ArchiveDeviceFacts
{
    public IReadOnlyDictionary<string, string?> ExtraCodes { get; init; } =
        new Dictionary<string, string?>();
}
