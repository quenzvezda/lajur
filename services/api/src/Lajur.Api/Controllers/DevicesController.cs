using Lajur.Api.Auth;
using Lajur.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lajur.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/devices")]
public sealed class DevicesController(IAuthService authService) : ControllerBase
{
    [HttpDelete("{deviceId:guid}")]
    public async Task<IActionResult> Revoke(Guid deviceId, CancellationToken cancellationToken)
    {
        if (!User.TryGetSessionIds(out var userId, out _, out _))
        {
            return Unauthorized();
        }

        var found = await authService.RevokeDeviceAsync(userId, deviceId, cancellationToken);
        return found ? NoContent() : NotFound();
    }
}
