namespace AmcoLogs.Core.Device;

public static class DeviceCode
{
    public const string FixedPrefix = "36";
    public const string MobilePrefix = "4";

    public static string Digits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return new string(raw.Where(char.IsDigit).ToArray());
    }

    public static DeviceMobility Classify(string? raw)
    {
        var digits = Digits(raw);
        if (digits.StartsWith(FixedPrefix, StringComparison.Ordinal))
        {
            return DeviceMobility.Fixed;
        }

        if (digits.StartsWith(MobilePrefix, StringComparison.Ordinal))
        {
            return DeviceMobility.Mobile;
        }

        return DeviceMobility.Unknown;
    }

    public static string Label(DeviceMobility mobility) => mobility switch
    {
        DeviceMobility.Fixed => "Σταθερό",
        DeviceMobility.Mobile => "Κινητό",
        _ => "Άγνωστο"
    };
}
