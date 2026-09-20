using System.Security.Claims;

namespace Lajur.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetSessionIds(
        this ClaimsPrincipal principal,
        out Guid userId,
        out Guid deviceId,
        out Guid sessionFamilyId)
    {
        userId = default;
        deviceId = default;
        sessionFamilyId = default;
        return Guid.TryParse(principal.FindFirstValue("sub"), out userId) &&
               Guid.TryParse(principal.FindFirstValue("device_id"), out deviceId) &&
               Guid.TryParse(principal.FindFirstValue("session_family_id"), out sessionFamilyId);
    }
}
