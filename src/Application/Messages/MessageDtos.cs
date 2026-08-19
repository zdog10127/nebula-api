namespace DiscordClone.Application.Messages;

public record SendMessageRequest(string Content, IReadOnlyList<Guid>? AttachmentIds = null);

public record EditMessageRequest(string Content);

public record AttachmentSummary(Guid Id, string FileName, string ContentType, long SizeBytes, string Url);

public record ReactionSummary(string Emoji, IReadOnlyList<Guid> UserIds);

public record AddReactionRequest(string Emoji);

public record MessageDto(
    Guid Id,
    Guid ChannelId,
    Guid AuthorId,
    string AuthorUsername,
    string AuthorDisplayName,
    string? AuthorAvatarUrl,
    string Content,
    DateTime CreatedAt,
    DateTime? EditedAt,
    IReadOnlyList<AttachmentSummary> Attachments,
    IReadOnlyList<ReactionSummary> Reactions,
    IReadOnlyList<Guid> MentionedUserIds,
    bool IsPinned);

public record UnreadCountDto(int Count, bool HasMention);
