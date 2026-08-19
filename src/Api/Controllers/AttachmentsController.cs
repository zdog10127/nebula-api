using DiscordClone.Api.Common;
using DiscordClone.Application.Attachments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DiscordClone.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class AttachmentsController : ControllerBase
{
    private const long MaxRequestBodySize = 26_214_400; // 25 MB

    private readonly IAttachmentService _attachmentService;

    public AttachmentsController(IAttachmentService attachmentService)
    {
        _attachmentService = attachmentService;
    }

    [HttpPost("attachments")]
    [RequestSizeLimit(MaxRequestBodySize)]
    public async Task<ActionResult<AttachmentDto>> Upload(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await _attachmentService.UploadAsync(User.GetUserId(), stream, file.FileName, file.ContentType, file.Length, ct);
        return Ok(result);
    }

    [HttpPost("users/me/avatar")]
    [RequestSizeLimit(MaxRequestBodySize)]
    public async Task<ActionResult<object>> UploadAvatar(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var url = await _attachmentService.UploadAvatarAsync(User.GetUserId(), stream, file.FileName, file.ContentType, file.Length, ct);
        return Ok(new { avatarUrl = url });
    }

    [HttpPost("users/me/banner")]
    [RequestSizeLimit(MaxRequestBodySize)]
    public async Task<ActionResult<object>> UploadBanner(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var url = await _attachmentService.UploadBannerAsync(User.GetUserId(), stream, file.FileName, file.ContentType, file.Length, ct);
        return Ok(new { bannerUrl = url });
    }
}
