using System.Text.Json;

namespace ArknightsWorkshop;

public class Config
{
    public int MaxConcurrentDownloads { get; set; } = 4;
    public bool KeepIntermediateData { get; set; } = true;
    public string WorkingDirectory { get; set; } = Path.Combine(Util.ExecutableFolder!, "resources");

    public static Config Read()
    {
        if (Util.ExecutableFolder is null) throw new("Couldn't get executable path");
        var path = Path.Combine(Util.ExecutableFolder, "config.json");
        if (!File.Exists(path)) return new();
        var raw = File.ReadAllText(path);
        return JsonSerializer.Deserialize(raw, SourceGenerationContext.Default.Config) ?? new();
    }
}
