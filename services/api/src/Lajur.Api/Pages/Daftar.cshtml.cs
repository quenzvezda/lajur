using System.ComponentModel.DataAnnotations;
using Lajur.Application.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Lajur.Api.Pages;

[EnableRateLimiting("account")]
[RequestSizeLimit(4096)]
public sealed class DaftarModel(IAccountSelfService accounts) : PageModel
{
    [BindProperty, Required, StringLength(64)] public string Username { get; set; } = "";
    [BindProperty, Required, StringLength(256, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [BindProperty, Compare(nameof(Password), ErrorMessage = "Konfirmasi password tidak cocok."), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
    [BindProperty, StringLength(128)] public string? InvitationCode { get; set; }
    public string Mode => accounts.Policy.Mode;
    public IReadOnlyList<string>? Codes { get; private set; }
    public string? Error { get; private set; }
    public void OnGet() { }
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) { Error = "Periksa isian: password minimal 12 karakter dan konfirmasi harus cocok."; ClearPasswords(); return Page(); }
        var result = await accounts.RegisterAsync(new(Username, Password, InvitationCode ?? ""), cancellationToken);
        ClearPasswords();
        if (result.Succeeded) Codes = result.Value!.Codes;
        else Error = result.Failure switch
        {
            AuthFailure.UsernameAlreadyExists => "Username tidak tersedia.",
            AuthFailure.InvalidInvitation => "Undangan tidak valid, sudah terpakai, atau kedaluwarsa.",
            AuthFailure.InvalidUsername => "Gunakan username 3–64 karakter: huruf, angka, titik, _ atau -.",
            _ => "Pendaftaran gagal. Periksa isian atau coba lagi nanti."
        };
        return Page();
    }
    private void ClearPasswords()
    {
        Password = ConfirmPassword = "";
        ModelState.Remove(nameof(Password)); ModelState.Remove(nameof(ConfirmPassword));
    }
}
