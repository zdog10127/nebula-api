namespace DiscordClone.Application.Music;

public interface IMusicService
{
    // Accepts either a pasted link (YouTube watch/share/shorts URL) or a plain search
    // term (e.g. "artist - song name") and resolves it to a specific video.
    Task<MusicResolveResult> ResolveAsync(string query, CancellationToken ct);
}
