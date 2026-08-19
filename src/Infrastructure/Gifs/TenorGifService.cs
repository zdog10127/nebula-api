using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DiscordClone.Application.Common;
using DiscordClone.Application.Gifs;

namespace DiscordClone.Infrastructure.Gifs;

public class TenorGifService : IGifService
{
    private const string BaseUrl = "https://tenor.googleapis.com/v2";

    private readonly HttpClient _http;
    private readonly TenorOptions _options;

    public TenorGifService(HttpClient http, TenorOptions options)
    {
        _http = http;
        _options = options;
    }

    public Task<GifSearchResult> SearchAsync(string query, string? pos, CancellationToken ct)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new AppException("Search query is required.");

        var url = $"{BaseUrl}/search?q={Uri.EscapeDataString(trimmed)}&client_key=discordclone&limit=24&media_filter=gif"
            + (string.IsNullOrEmpty(pos) ? "" : $"&pos={Uri.EscapeDataString(pos)}");

        return FetchAsync(url, ct);
    }

    public Task<GifSearchResult> GetTrendingAsync(string? pos, CancellationToken ct)
    {
        var url = $"{BaseUrl}/featured?client_key=discordclone&limit=24&media_filter=gif"
            + (string.IsNullOrEmpty(pos) ? "" : $"&pos={Uri.EscapeDataString(pos)}");

        return FetchAsync(url, ct);
    }

    private async Task<GifSearchResult> FetchAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new AppException("A busca de GIFs não está configurada neste servidor.", 503);

        var response = await _http.GetFromJsonAsync<TenorResponse>($"{url}&key={Uri.EscapeDataString(_options.ApiKey)}", ct)
            ?? throw new AppException("Falha ao buscar GIFs.", 502);

        var results = (response.Results ?? [])
            .Where(r => r.MediaFormats.Gif is not null)
            .Select(r =>
            {
                var gif = r.MediaFormats.Gif!;
                var preview = r.MediaFormats.Tinygif ?? gif;
                return new GifResultDto(r.Id, preview.Url, gif.Url, gif.Dims.ElementAtOrDefault(0), gif.Dims.ElementAtOrDefault(1));
            })
            .ToList();

        return new GifSearchResult(results, string.IsNullOrEmpty(response.Next) ? null : response.Next);
    }

    private record TenorResponse(
        [property: JsonPropertyName("results")] List<TenorResult>? Results,
        [property: JsonPropertyName("next")] string? Next);

    private record TenorResult(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("media_formats")] TenorMediaFormats MediaFormats);

    private record TenorMediaFormats(
        [property: JsonPropertyName("gif")] TenorMedia? Gif,
        [property: JsonPropertyName("tinygif")] TenorMedia? Tinygif);

    private record TenorMedia(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("dims")] List<int> Dims);
}
