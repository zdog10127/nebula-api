namespace DiscordClone.Application.Gifs;

public interface IGifService
{
    Task<GifSearchResult> SearchAsync(string query, string? pos, CancellationToken ct);
    Task<GifSearchResult> GetTrendingAsync(string? pos, CancellationToken ct);
}
