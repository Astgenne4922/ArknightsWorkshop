using System.Text.Json.Serialization;

namespace ArknightsWorkshop.Data;

public class NetworkConfig
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = null!;
}