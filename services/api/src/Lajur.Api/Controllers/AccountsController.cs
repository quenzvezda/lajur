using System.ComponentModel.DataAnnotations;
using Lajur.Api.Auth;
using Lajur.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lajur.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[RequestSizeLimit(4096)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("account")]
public sealed class AccountsController(IAccountSelfService accounts) : ControllerBase
{
    [HttpGet("registration")]
    [AllowAnonymous]
    public ActionResult<RegistrationPolicy> Policy() => Ok(accounts.Policy);

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegistrationRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.RegisterAsync(new(request.Username, request.Password, request.InvitationCode), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Failure(result.Failure);
    }

    [HttpPost("recover")]
    [AllowAnonymous]
    public async Task<IActionResult> Recover(RecoveryRequest request, CancellationToken cancellationToken) =>
        await accounts.RecoverAsync(request.Username, request.Code, request.NewPassword, cancellationToken)
            ? NoContent() : Failure(AuthFailure.InvalidCredentials);

    [HttpPost("password")]
    [Authorize]
    public async Task<IActionResult> Password(ChangePasswordRequest request, CancellationToken cancellationToken) =>
        User.TryGetSessionIds(out var id, out _, out _) &&
        await accounts.ChangePasswordAsync(id, request.Password, request.NewPassword, cancellationToken)
            ? NoContent() : Failure(AuthFailure.InvalidCredentials);

    [HttpPost("recovery-codes")]
    [Authorize]
    public async Task<IActionResult> Codes(PasswordConfirmation request, CancellationToken cancellationToken)
    {
        if (!User.TryGetSessionIds(out var id, out _, out _)) return Unauthorized();
        var result = await accounts.RegenerateCodesAsync(id, request.Password, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Failure(result.Failure);
    }

    [HttpPost("invitations")]
    [Authorize]
    public async Task<IActionResult> Invite(PasswordConfirmation request, CancellationToken cancellationToken)
    {
        if (!User.TryGetSessionIds(out var id, out _, out _)) return Unauthorized();
        var result = await accounts.InviteAsync(id, request.Password, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Failure(result.Failure);
    }

    private ObjectResult Failure(AuthFailure? reason) => Problem(statusCode: 400, title: "Permintaan akun gagal",
        detail: reason switch
        {
            AuthFailure.InvalidPassword => "Password harus 12–256 karakter.",
            AuthFailure.InvalidUsername => "Username harus 3–64 karakter: huruf, angka, titik, garis bawah, atau tanda minus.",
            AuthFailure.UsernameAlreadyExists => "Username tidak tersedia. Pilih username lain.",
            AuthFailure.InvalidInvitation => "Kode undangan tidak valid, sudah terpakai, atau kedaluwarsa.",
            AuthFailure.RegistrationUnavailable => "Pendaftaran belum tersedia atau batas akun sudah tercapai.",
            AuthFailure.InvitationLimit => "Maksimal 5 undangan aktif. Tunggu undangan digunakan atau kedaluwarsa.",
            _ => "Username, password, atau kode pemulihan tidak valid."
        }, extensions: new Dictionary<string, object?> { ["code"] = "account_request_failed" });
}

public sealed record RegistrationRequest(
    [Required, StringLength(64)] string Username,
    [Required, StringLength(256, MinimumLength = 12)] string Password,
    [StringLength(128)] string InvitationCode = "");
public sealed record RecoveryRequest(
    [Required, StringLength(64)] string Username,
    [Required, StringLength(128)] string Code,
    [Required, StringLength(256, MinimumLength = 12)] string NewPassword);
public sealed record ChangePasswordRequest(
    [Required, StringLength(256)] string Password,
    [Required, StringLength(256, MinimumLength = 12)] string NewPassword);
public sealed record PasswordConfirmation([Required, StringLength(256)] string Password);
