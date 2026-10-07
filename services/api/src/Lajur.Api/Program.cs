using Lajur.Api.Auth;
using Lajur.Api.Errors;
using Lajur.Api.Health;
using Lajur.Api.Web;
using Lajur.Application.Auth;
using Lajur.Infrastructure.Auth;
using Lajur.Infrastructure.Finance;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<FinanceExceptionHandler>();
builder.Services.AddLajurAuthPersistence(builder.Configuration);
builder.Services.AddLajurFinance();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Request validation failed"
        };
        problem.Extensions["code"] = "request_validation_failed";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem)
        {
            ContentTypes = { "application/problem+json" }
        };
    };
});

builder.Services.AddSingleton(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var signingKey = new SymmetricSecurityKey(JwtSigningKeyStore.LoadOrCreate(configuration));
    return new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
});
builder.Services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>, SigningCredentials>((options, configuredAuth, signingCredentials) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuredAuth.Value.Issuer,
            ValidateAudience = true,
            ValidAudience = configuredAuth.Value.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingCredentials.Key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "unique_name"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (context.Principal is null ||
                    !context.Principal.TryGetSessionIds(out var userId, out var deviceId, out var familyId))
                {
                    context.Fail("Access token session claims are invalid.");
                    return;
                }

                var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
                var session = await authService.GetCurrentSessionAsync(
                    userId,
                    deviceId,
                    familyId,
                    context.HttpContext.RequestAborted);
                if (session is null)
                {
                    context.Fail("Access token session is revoked.");
                }
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "about:blank",
                    title = "Authentication required",
                    status = StatusCodes.Status401Unauthorized,
                    code = "authentication_required",
                    traceId = context.HttpContext.TraceIdentifier
                });
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddLajurWebViewer(builder.Configuration, builder.Environment);

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("refresh", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (!context.HttpContext.Request.Path.StartsWithSegments("/api"))
        {
            context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
            await context.HttpContext.Response.WriteAsync(
                "Terlalu banyak percobaan masuk. Tunggu satu menit lalu coba lagi.",
                cancellationToken);
            return;
        }

        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title = "Too many requests",
            status = StatusCodes.Status429TooManyRequests,
            code = "rate_limit_exceeded",
            traceId = context.HttpContext.TraceIdentifier
        }, cancellationToken);
    };
});

builder.Services.AddHealthChecks()
    .AddCheck<AuthDatabaseHealthCheck>("auth-database", tags: ["ready"]);

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AuthDatabaseInitializer>().InitializeAsync();
}

if (await OperatorAccountCommand.TryRunAsync(args, app.Services))
{
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseLajurWebSecurityHeaders();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();

public partial class Program;
