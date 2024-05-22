using System.Text.Json.Serialization;

namespace ArknightsWorkshop.Data;

public class PackInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = null!;

    [JsonPropertyName("md5")]
    public string MD5 { get; set; } = null!;

    [JsonPropertyName("totalSize")]
    public long TotalSize { get; set; }

    [JsonPropertyName("abSize")]
    public long ABSize { get; set; }

    [JsonPropertyName("cid")]
    public long CID { get; set; }
}