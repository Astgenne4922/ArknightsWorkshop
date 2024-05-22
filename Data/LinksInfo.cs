using System.Text.Json.Serialization;

namespace ArknightsWorkshop.Data;

public class LinksInfo
{
    [JsonPropertyName("network")]
    public LinksList Network { get; set; } = null!;
}