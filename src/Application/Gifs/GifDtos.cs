namespace DiscordClone.Application.Gifs;

public record GifResultDto(string Id, string PreviewUrl, string Url, int Width, int Height);

public record GifSearchResult(IReadOnlyList<GifResultDto> Results, string? Next);
