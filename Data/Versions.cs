using System.Text.Json.Serialization;

namespace ArknightsWorkshop.Data;

public class Versions
{
    [JsonPropertyName("resVersion")]
    public string Resources { get; set; } = null!;

    [JsonPropertyName("clientVersion")]
    public string Client { get; set; } = null!;

}