using System.Text.Json.Serialization;

namespace ArknightsWorkshop.Data;

public class LinksConfig
{
    [JsonPropertyName("configs")]
    public Dictionary<string, LinksInfo> Configs { get; set; } = null!;

}