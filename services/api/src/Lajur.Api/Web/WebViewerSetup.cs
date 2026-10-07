using System.Security.Claims;
using System.Threading.RateLimiting;
using Lajur.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace Lajur.Api.Web;

/// <summary>
/// Read-only browser workbook viewer: Razor Pages + a cookie scheme that is separate from the Android
/// JWT scheme. Browser sign-in never creates, rotates or revokes Android device sessions (ADR 0008).
/// </summary>
public static class WebViewerSetup
{
    public const string Scheme = "LajurWeb";
    public const string Policy = "LajurWeb";
    public const string LoginRateLimitPolicy = "web-login";
    public const string UserIdClaim = "lajur:uid";
    public const string StampClaim = "lajur:web_stamp";

    public static IServiceCollection AddLajurWebViewer(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var storageRoot = configuration["Lajur:Storage:RootPath"] ?? ".local/lajur";
        services.AddDataProtection()
            .SetApplicationName("Lajur")
            .PersistKeysToFileSystem(new DirectoryInfo(
                Path.GetFullPath(Path.Combine(storageRoot, "keys", "data-protection"))));

        services.AddAuthentication()
            .AddCookie(Scheme, options =>
            {
                options.Cookie.Name = "__Host-lajur-web";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
                options.LoginPath = "/masuk";
                options.LogoutPath = "/keluar";
                options.AccessDeniedPath = "/masuk";
                options.ReturnUrlParameter = "kembali";
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = ValidatePrincipalAsync,
                    OnRedirectToLogin = context =>
                    {
                        if (IsApiRequest(context.Request))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }

                        context.Response.Redirect(context.RedirectUri);
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(UserIdClaim));

        services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/", Policy);
            options.Conventions.AllowAnonymousToPage("/Masuk");
        });

        services.AddRateLimiter(options =>
        {
            // Only credential submissions count; rendering the login form is free.
            options.AddPolicy(LoginRateLimitPolicy, context =>
                HttpMethods.IsPost(context.Request.Method)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 5,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        })
                    : RateLimitPartition.GetNoLimiter("web-login-get"));
        });

        return services;
    }

    /// <summary>Security headers for HTML pages. Financial pages are never cached.</summary>
    public static IApplicationBuilder UseLajurWebSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!IsApiRequest(context.Request) && !context.Request.Path.StartsWithSegments("/health"))
            {
                context.Response.OnStarting(() =>
                {
                    var headers = context.Response.Headers;
                    headers["X-Content-Type-Options"] = "nosniff";
                    headers["Referrer-Policy"] = "no-referrer";
                    headers["X-Frame-Options"] = "DENY";
                    if (context.Response.ContentType?.StartsWith("text/html", StringComparison.Ordinal) == true)
                    {
                        headers["Content-Security-Policy"] =
                            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
                            "object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
                        headers.CacheControl = "no-store";
                    }

                    return Task.CompletedTask;
                });
            }

            await next();
        });

    public static ClaimsPrincipal CreatePrincipal(BrowserIdentity identity) =>
        new(new ClaimsIdentity(
            [
                new Claim(UserIdClaim, identity.UserId.ToString("D")),
                new Claim(ClaimTypes.Name, identity.Username),
                new Claim(StampClaim, identity.SecurityStamp)
            ],
            Scheme));

    public static bool TryGetWebUserId(this ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(UserIdClaim), out userId);

    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is null || !context.Principal.TryGetWebUserId(out var userId))
        {
            await RejectAsync(context);
            return;
        }

        var browserAuth = context.HttpContext.RequestServices.GetRequiredService<IBrowserAuthService>();
        var identity = await browserAuth.GetActiveIdentityAsync(userId, context.HttpContext.RequestAborted);
        if (identity is null
            || !string.Equals(identity.SecurityStamp, context.Principal.FindFirstValue(StampClaim), StringComparison.Ordinal))
        {
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(Scheme);
    }

    private static bool IsApiRequest(HttpRequest request) => request.Path.StartsWithSegments("/api");
}
