using System.ComponentModel.DataAnnotations;
using Lajur.Api.Web;
using Lajur.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Lajur.Api.Pages;

/// <summary>
/// Browser sign-in for the read-only workbook viewer. Uses <see cref="IBrowserAuthService"/>, which only
/// verifies the password: Android device sessions are neither created nor revoked.
/// </summary>
[EnableRateLimiting(WebViewerSetup.LoginRateLimitPolicy)]
public sealed class MasukModel(IBrowserAuthService browserAuth) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Nama pengguna wajib diisi.")]
    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Kata sandi wajib diisi.")]
    [MaxLength(256)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true, Name = "kembali")]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true && User.TryGetWebUserId(out _))
        {
            return LocalRedirect(SafeReturnUrl());
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var identity = await browserAuth.VerifyCredentialsAsync(Username, Password, cancellationToken);
        Password = string.Empty;
        if (identity is null)
        {
            ErrorMessage = "Nama pengguna atau kata sandi salah.";
            return Page();
        }

        await HttpContext.SignInAsync(
            WebViewerSetup.Scheme,
            WebViewerSetup.CreatePrincipal(identity),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
        return LocalRedirect(SafeReturnUrl());
    }

    private string SafeReturnUrl() =>
        !string.IsNullOrEmpty(ReturnUrl)
        && Url.IsLocalUrl(ReturnUrl)
        && !ReturnUrl.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            ? ReturnUrl
            : "/buku";
}
