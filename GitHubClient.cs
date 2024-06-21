using System.Text.Json;

namespace ArknightsWorkshop;

public class GitHubClient : IDisposable
{
    private const string url = "https://api.github.com";
    private const string raw = "https://raw.githubusercontent.com";

    private readonly HttpClient _http = new()
    {
        DefaultRequestHeaders = {{ "User-Agent", "some tool" }}
    };

    public HttpClient Http => _http;

    public JsonDocument Releases(string owner, string repo) =>
        GetJsonDoc($"{url}/repos/{owner}/{repo}/releases");

    public JsonDocument FileTree(string owner, string repo, string branch) =>
        GetJsonDoc($"{url}/repos/{owner}/{repo}/git/trees/{branch}?recursive=1");

    public void RawFile(string owner, string repo, string branch, string path, Stream to)
    {
        var url = $"{raw}/{owner}/{repo}/{branch}/{path}";
        using var resp = Http.Send(new(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        resp.Content.ReadAsStream().CopyTo(to); 
    }

    private JsonDocument GetJsonDoc(string url)
    {
        using var resp = _http.Send(new(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(resp.Content.ReadAsStream());
    }

    public void Dispose() => _http.Dispose();
}
