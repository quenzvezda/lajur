using System.ComponentModel.DataAnnotations;
using Lajur.Application.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Lajur.Api.Pages;

[EnableRateLimiting("account")]
[RequestSizeLimit(4096)]
public sealed class PulihkanModel(IAccountSelfService accounts) : PageModel
{
    [BindProperty, Required, StringLength(64)] public string Username { get; set; } = "";
    [BindProperty, Required, StringLength(128)] public string Code { get; set; } = "";
    [BindProperty, Required, StringLength(256, MinimumLength = 12), DataType(DataType.Password)] public string NewPassword { get; set; } = "";
    [BindProperty, Compare(nameof(NewPassword), ErrorMessage = "Konfirmasi password tidak cocok."), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
    public bool Completed { get; private set; }
    public string? Error { get; private set; }
    public void OnGet() { }
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            Completed = await accounts.RecoverAsync(Username, Code, NewPassword, cancellationToken);
            if (!Completed) Error = "Username atau kode pemulihan tidak valid.";
        }
        else Error = "Periksa isian: password minimal 12 karakter dan konfirmasi harus cocok.";
        Code = NewPassword = ConfirmPassword = "";
        ModelState.Remove(nameof(Code)); ModelState.Remove(nameof(NewPassword)); ModelState.Remove(nameof(ConfirmPassword));
        return Page();
    }
}
