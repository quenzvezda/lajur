using Lajur.Api.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Lajur.Api.Pages;

/// <summary>Browser sign-out. POST-only with antiforgery; affects only this browser's cookie.</summary>
public sealed class KeluarModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Buku");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(WebViewerSetup.Scheme);
        return RedirectToPage("/Masuk");
    }
}
