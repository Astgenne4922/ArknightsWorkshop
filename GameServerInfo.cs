using ArknightsWorkshop.Data;
using System.Net.Http.Json;
using System.Text.Json;

namespace ArknightsWorkshop;

public class GameServerInfo(string resourceVersion, string clientVersion, string assetsUrl)
{
    public static readonly string China = "https://ak-conf.hypergryph.com/config/prod/official/network_config";
    public static readonly string Global = "https://ark-us-static-online.yo-star.com/assetbundle/official/network_config";
    public static readonly string Platform = "Android";

    public string ResourceVersion => resourceVersion;
    public string ClientVersion => clientVersion;
    public string AssetsUrl => $"{assetsUrl}/{Platform}/assets/{ResourceVersion}";


    public static async ValueTask<GameServerInfo> Fetch(string url)
    {
        using var http = Util.MakeAkClient();
        // Fetch 'network_config'
        var rawConfig = (await http.GetFromJsonAsync(url, SourceGenerationContext.Default.NetworkConfig)).NotNullJson();
        var config = JsonSerializer.Deserialize(rawConfig.Content, SourceGenerationContext.Default.LinksConfig).NotNullJson();
        var links = config.Configs.Values.FirstOrDefault(HasAllLinks)?.Network ?? throw new("no valid link config");
        // Fetch 'versions'
        var versionUrl = links.VersionUrl.Replace("{0}", Platform);
        var versions = (await http.GetFromJsonAsync(versionUrl, SourceGenerationContext.Default.Versions)).NotNullJson();
        // Done
        return new(versions.Resources, versions.Client, links.AssetsUrl);

        static bool HasAllLinks(LinksInfo p) =>
            p.Network.VersionUrl is not null && p.Network.AssetsUrl is not null;
    }

    public enum Server
    {
        Global,
        China
    }
}