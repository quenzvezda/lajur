using System.ComponentModel.DataAnnotations;
using Lajur.Api.Web;
using Lajur.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Lajur.Api.Pages;

[EnableRateLimiting("account")]
[RequestSizeLimit(4096)]
public sealed class AkunModel(IAccountSelfService accounts) : PageModel
{
    [BindProperty, Required, StringLength(256), DataType(DataType.Password)] public string Password { get; set; } = "";
    [BindProperty, StringLength(256), DataType(DataType.Password)] public string? NewPassword { get; set; }
    [BindProperty, StringLength(256), DataType(DataType.Password)] public string? ConfirmPassword { get; set; }
    public IReadOnlyList<string>? Codes { get; private set; }
    public InvitationReceipt? Invitation { get; private set; }
    public string? Error { get; private set; }
    public bool InvitesEnabled => accounts.Policy.Mode == "invite";
    public void OnGet() { }
    public async Task<IActionResult> OnPostAsync(string action, CancellationToken cancellationToken)
    {
        if (!User.TryGetWebUserId(out var id)) return Unauthorized();
        if (ModelState.IsValid)
        {
            switch (action)
            {
                case "codes":
                    var codes = await accounts.RegenerateCodesAsync(id, Password, cancellationToken);
                    if (codes.Succeeded) Codes = codes.Value!.Codes;
                    else Error = "Password tidak valid.";
                    break;
                case "invite":
                    var invite = await accounts.InviteAsync(id, Password, cancellationToken);
                    if (invite.Succeeded) Invitation = invite.Value;
                    else Error = invite.Failure == AuthFailure.InvitationLimit ? "Maksimal 5 undangan aktif." : "Undangan tidak dapat dibuat. Periksa password.";
                    break;
                case "password":
                    if (NewPassword is null || NewPassword.Length is < 12 or > 256 || NewPassword != ConfirmPassword)
                        Error = "Password baru minimal 12 karakter dan konfirmasi harus cocok.";
                    else if (await accounts.ChangePasswordAsync(id, Password, NewPassword, cancellationToken))
                    {
                        await HttpContext.SignOutAsync(WebViewerSetup.Scheme);
                        return Redirect("/masuk");
                    }
                    else Error = "Password saat ini tidak valid.";
                    break;
                default: return BadRequest();
            }
        }
        Password = NewPassword = ConfirmPassword = "";
        ModelState.Remove(nameof(Password)); ModelState.Remove(nameof(NewPassword)); ModelState.Remove(nameof(ConfirmPassword));
        return Page();
    }
}
