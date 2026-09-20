using Lajur.Api.Auth;
using Lajur.Application.Auth;
using Lajur.Application.Workbooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lajur.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/me")]
public sealed class MeController(IAuthService authService, IWorkbookService workbookService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken cancellationToken)
    {
        if (!User.TryGetSessionIds(out var userId, out var deviceId, out var familyId))
        {
            return Unauthorized();
        }

        var session = await authService.GetCurrentSessionAsync(userId, deviceId, familyId, cancellationToken);
        if (session is null)
        {
            return Unauthorized();
        }

        var workbooks = await workbookService.ListAsync(userId, cancellationToken);
        return Ok(new MeResponse(
            session.UserId,
            session.Username,
            session.DeviceId,
            session.DeviceLabel,
            session.SessionFamilyId,
            workbooks.Count == 0,
            workbooks.Select(workbook => workbook.Year).ToArray()));
    }
}

public sealed record MeResponse(
    Guid UserId,
    string Username,
    Guid DeviceId,
    string DeviceLabel,
    Guid SessionFamilyId,
    bool NeedsWorkbookOnboarding,
    IReadOnlyList<int> WorkbookYears);
