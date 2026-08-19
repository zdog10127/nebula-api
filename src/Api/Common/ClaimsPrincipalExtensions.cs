using System.Security.Claims;

namespace DiscordClone.Api.Common;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.Parse(value ?? throw new InvalidOperationException("User id claim not found."));
    }
}
