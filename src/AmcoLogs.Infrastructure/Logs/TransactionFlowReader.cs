using System.Text.RegularExpressions;
using AmcoLogs.Core.Logs;

namespace AmcoLogs.Infrastructure.Logs;

public sealed class TransactionFlowReader : ITransactionFlowReader
{
    private const int MaxLines = 20;
    private readonly string _folder;

    public TransactionFlowReader(string folder)
    {
        _folder = folder;
    }

    public FlowReading Read(IReadOnlyList<string> transactionLines, string? profile)
    {
        var scope = ScopeOf(profile);
        var root = Path.Combine(_folder, scope.Key);
        var lines = (transactionLines ?? [])
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        var steps = LoadLines(Path.Combine(root, "archetype.txt"));
        var samples = LoadErrorSamples(lines, Path.Combine(root, "errors"));
        var matched = CountMatched(steps, lines, out var deviations);
        return new FlowReading
        {
            HasArchetype = steps.Count > 0,
            StepCount = steps.Count,
            MatchedSteps = matched,
            ErrorKind = BestKind(samples),
            FlowMessage = FlowMessage(scope, steps, lines, deviations),
            ErrorMessage = ErrorMessage(scope, samples),
            Deviations = deviations,
            Readings = samples
        };
    }

    private readonly record struct FlowScope(string Key, string Label);

    private static FlowScope ScopeOf(string? profile) =>
        string.Equals(profile, "OASA", StringComparison.OrdinalIgnoreCase)
            ? new FlowScope("oasa", "ΟΑΣΑ")
            : new FlowScope("oseth", "OSETH");

    private static int CountMatched(
        IReadOnlyList<string> steps,
        IReadOnlyList<string> lines,
        out IReadOnlyList<FlowDeviation> deviations)
    {
        var missed = new List<FlowDeviation>();
        var matched = 0;
        var cursor = 0;
        for (var index = 0; index < steps.Count; index++)
        {
            var foundAt = -1;
            for (var line = cursor; line < lines.Count; line++)
            {
                if (FlowText.Matches(steps[index], lines[line]))
                {
                    foundAt = line;
                    break;
                }
            }

            if (foundAt < 0)
            {
                missed.Add(new FlowDeviation
                {
                    StepNumber = index + 1,
                    Expected = steps[index]
                });
                continue;
            }

            matched++;
            cursor = foundAt + 1;
        }

        deviations = missed;
        return matched;
    }

    private static string? BestKind(IReadOnlyList<ErrorSampleReading> samples)
    {
        var best = samples
            .Where(sample => sample.MatchedCount > 0)
            .OrderByDescending(sample => sample.MatchedCount)
            .ThenBy(sample => sample.ErrorKind, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return best?.ErrorKind;
    }

    private static List<ErrorSampleReading> LoadErrorSamples(IReadOnlyList<string> lines, string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var readings = new List<ErrorSampleReading>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.txt").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var kind = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(kind) || kind.StartsWith('_') || kind.StartsWith('.'))
            {
                continue;
            }

            var samples = LoadLines(file);
            if (samples.Count == 0)
            {
                continue;
            }

            var matched = samples.Count(sample => lines.Any(line => FlowText.Matches(sample, line)));
            readings.Add(new ErrorSampleReading
            {
                ErrorKind = kind,
                SampleCount = samples.Count,
                MatchedCount = matched
            });
        }

        return readings;
    }

    private static string FlowMessage(
        FlowScope scope,
        IReadOnlyList<string> steps,
        IReadOnlyList<string> lines,
        IReadOnlyList<FlowDeviation> deviations)
    {
        if (steps.Count == 0)
        {
            return $"Το αρχέτυπο flow του {scope.Label} είναι άδειο. Γράψε ως 20 γραμμές, με τη σειρά, στο data/flow/{scope.Key}/archetype.txt.";
        }

        if (lines.Count == 0)
        {
            return $"Δεν υπάρχουν γραμμές συναλλαγής για σύγκριση με το αρχέτυπο του {scope.Label}.";
        }

        var matched = steps.Count - deviations.Count;
        return deviations.Count == 0
            ? $"Η συναλλαγή ακολουθεί και τα {steps.Count} βήματα του αρχέτυπου {scope.Label}."
            : $"Η συναλλαγή αποκλίνει από το {scope.Label}: βρέθηκαν {matched} από {steps.Count} βήματα.";
    }

    private static string ErrorMessage(FlowScope scope, IReadOnlyList<ErrorSampleReading> samples)
    {
        if (samples.Count == 0)
        {
            return $"Δεν έχουν μπει ακόμη τα logs σφάλματος του {scope.Label}. Βάλε ως 20 γραμμές σε ένα αρχείο μέσα στο data/flow/{scope.Key}/errors.";
        }

        var best = samples
            .Where(sample => sample.MatchedCount > 0)
            .OrderByDescending(sample => sample.MatchedCount)
            .ThenBy(sample => sample.ErrorKind, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (best is null)
        {
            return "Κανένα από τα δείγματα σφάλματος δεν εμφανίζεται σε αυτή τη συναλλαγή.";
        }

        return $"Προκύπτει {best.ErrorKind}: ταίριαξαν {best.MatchedCount} από {best.SampleCount} δείγματα.";
    }

    private static IReadOnlyList<string> LoadLines(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith("//"))
            .Take(MaxLines)
            .ToArray();
    }
}

internal static class FlowText
{
    private static readonly Regex GuidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);
    private static readonly Regex NumberPattern = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex SpacePattern = new(@"\s+", RegexOptions.Compiled);

    public static bool Matches(string sample, string actual)
    {
        var left = Normalize(sample);
        var right = Normalize(actual);
        if (left.Length < 6 || right.Length < 6)
        {
            return false;
        }

        return right.Contains(left, StringComparison.Ordinal)
            || left.Contains(right, StringComparison.Ordinal);
    }

    public static string Normalize(string raw)
    {
        var message = MessageOf(raw).ToLowerInvariant();
        message = GuidPattern.Replace(message, "#");
        message = NumberPattern.Replace(message, "#");
        return SpacePattern.Replace(message, " ").Trim();
    }

    private static string MessageOf(string raw)
    {
        var first = raw.IndexOf('|');
        if (first < 0)
        {
            return raw.Trim();
        }

        var second = raw.IndexOf('|', first + 1);
        if (second < 0)
        {
            return raw.Trim();
        }

        var third = raw.IndexOf('|', second + 1);
        return third < 0 ? raw.Trim() : raw[(third + 1)..].Trim();
    }
}
