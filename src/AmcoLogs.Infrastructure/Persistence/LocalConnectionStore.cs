using System.Text.Json;
using AmcoLogs.Core.Persistence;

namespace AmcoLogs.Infrastructure.Persistence;

public sealed class LocalConnectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public LocalConnectionStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AmcoLogs");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "mysql-connection.json");
    }

    public MySqlConnectionSettings? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<MySqlConnectionSettings>(json, JsonOptions);
    }

    public void Save(MySqlConnectionSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
