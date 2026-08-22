using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DiscordClone.Application.Common;
using DiscordClone.Application.Music;
using Microsoft.AspNetCore.WebUtilities;

namespace DiscordClone.Infrastructure.Music;

// No YouTube API key needed/configured: a pasted link is resolved to its video id directly
// (no network call), and a typed search term is resolved by pulling the first video id out of
// YouTube's own public search-results page — the same no-API-key technique long used by
// community YouTube search libraries. Either way, the human-readable title/thumbnail always
// comes from YouTube's official, stable oEmbed endpoint. The only fragile part is the search
// step: if YouTube changes their search-results page markup, that regex stops matching and
// typed searches fail (pasted links keep working regardless, since those never hit it).
public class YoutubeMusicService : IMusicService
{
    private static readonly Regex VideoIdInSearchResults = new(
        "\"videoRenderer\":\\{\"videoId\":\"([a-zA-Z0-9_-]{11})\"",
        RegexOptions.Compiled);

    private readonly HttpClient _http;

    public YoutubeMusicService(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
    }

    public async Task<MusicResolveResult> ResolveAsync(string query, CancellationToken ct)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new AppException("Digite o nome de uma música ou cole um link.");

        var videoId = ExtractVideoId(trimmed) ?? await SearchVideoIdAsync(trimmed, ct);
        if (videoId is null)
            throw new AppException("Nenhum resultado encontrado para essa busca.", 404);

        return await GetOEmbedAsync(videoId, ct);
    }

    private static string? ExtractVideoId(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return null;

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;

        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var id = uri.AbsolutePath.Trim('/').Split('/')[0];
            return string.IsNullOrEmpty(id) ? null : id;
        }

        if (host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || host.Equals("m.youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            if (uri.AbsolutePath == "/watch")
            {
                var parsed = QueryHelpers.ParseQuery(uri.Query);
                return parsed.TryGetValue("v", out var v) ? v.ToString() : null;
            }

            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length == 2 && (segments[0] == "shorts" || segments[0] == "embed" || segments[0] == "live"))
                return segments[1];
        }

        return null;
    }

    private async Task<string?> SearchVideoIdAsync(string query, CancellationToken ct)
    {
        var url = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}";

        string html;
        try
        {
            html = await _http.GetStringAsync(url, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AppException("Não foi possível buscar no YouTube agora.", 502);
        }

        var match = VideoIdInSearchResults.Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<MusicResolveResult> GetOEmbedAsync(string videoId, CancellationToken ct)
    {
        var watchUrl = $"https://www.youtube.com/watch?v={videoId}";
        var oembedUrl = $"https://www.youtube.com/oembed?url={Uri.EscapeDataString(watchUrl)}&format=json";

        OEmbedResponse? oembed;
        try
        {
            oembed = await _http.GetFromJsonAsync<OEmbedResponse>(oembedUrl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            oembed = null;
        }

        if (oembed is null)
            throw new AppException("Esse vídeo não está disponível ou foi removido.", 404);

        return new MusicResolveResult(videoId, oembed.Title, oembed.ThumbnailUrl);
    }

    private record OEmbedResponse(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("thumbnail_url")] string? ThumbnailUrl);
}
