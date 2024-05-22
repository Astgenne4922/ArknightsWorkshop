using System.Text.Json.Serialization;

public class LinksList
{
    [JsonPropertyName("hv")]
    public string VersionUrl { get; set; } = null!;

    [JsonPropertyName("hu")]
    public string AssetsUrl { get; set; } = null!;
}
