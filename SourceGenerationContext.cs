using ArknightsWorkshop;
using ArknightsWorkshop.Data;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(AbInfo))]
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(HotUpdateList))]
[JsonSerializable(typeof(LinksConfig))]
[JsonSerializable(typeof(LinksInfo))]
[JsonSerializable(typeof(LinksList))]
[JsonSerializable(typeof(NetworkConfig))]
[JsonSerializable(typeof(PackInfo))]
[JsonSerializable(typeof(Versions))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
