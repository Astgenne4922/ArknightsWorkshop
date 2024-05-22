using System.Text.Json.Serialization;

namespace ArknightsWorkshop.Data;

public class HotUpdateList
{
    [JsonPropertyName("abInfos")]
    public AbInfo[] ABInfos { get; set; } = null!;

    [JsonPropertyName("packInfos")]
    public PackInfo[] PackInfos { get; set; } = null!;
}
