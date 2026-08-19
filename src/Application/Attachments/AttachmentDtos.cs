namespace DiscordClone.Application.Attachments;

public record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, string Url);
