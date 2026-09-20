using Lajur.Api.Auth;
using Lajur.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lajur.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/me")]
public sealed class MeController(IAuthService authService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CurrentSession>> Get(CancellationToken cancellationToken)
    {
        if (!User.TryGetSessionIds(out var userId, out var deviceId, out var familyId))
        {
            return Unauthorized();
        }

        var session = await authService.GetCurrentSessionAsync(userId, deviceId, familyId, cancellationToken);
        return session is null ? Unauthorized() : Ok(session);
    }
}
