using System.Security.Claims;

namespace QTS.Infrastructure;

public static class ClaimsExtensions
{
    public static long CurrentUserId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub")
                  ?? user.FindFirstValue("userId");
        return long.TryParse(raw, out var id) ? id : 0;
    }

    public static string CurrentUserName(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? "unknown";
}
