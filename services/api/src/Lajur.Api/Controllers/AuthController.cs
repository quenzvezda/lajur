using Lajur.Api.Auth;
using Lajur.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lajur.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<AuthTokens>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(
            new LoginCommand(request.Username, request.Password, request.DeviceId, request.DeviceLabel),
            cancellationToken);
        return result.Succeeded ? Ok(result.Value) : AuthenticationProblem();
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("refresh")]
    public async Task<ActionResult<AuthTokens>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RefreshAsync(request.RefreshToken, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : AuthenticationProblem();
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!User.TryGetSessionIds(out var userId, out _, out var sessionFamilyId))
        {
            return AuthenticationProblem();
        }

        await authService.LogoutAsync(userId, sessionFamilyId, cancellationToken);
        return NoContent();
    }

    private ObjectResult AuthenticationProblem() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Authentication failed",
        detail: "The supplied credentials or session are invalid.",
        extensions: new Dictionary<string, object?> { ["code"] = "authentication_failed" });
}

public sealed record LoginRequest(string Username, string Password, Guid? DeviceId = null, string? DeviceLabel = null);
public sealed record RefreshRequest(string RefreshToken);
