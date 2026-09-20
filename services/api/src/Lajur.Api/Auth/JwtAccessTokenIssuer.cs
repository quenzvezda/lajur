using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Lajur.Application.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Lajur.Api.Auth;

public sealed class JwtAccessTokenIssuer(
    IOptions<AuthOptions> options,
    SigningCredentials signingCredentials,
    TimeProvider timeProvider) : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(
        Guid userId,
        string username,
        Guid deviceId,
        Guid sessionFamilyId)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(options.Value.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim("device_id", deviceId.ToString()),
            new Claim("session_family_id", sessionFamilyId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var token = new JwtSecurityToken(
            options.Value.Issuer,
            options.Value.Audience,
            claims,
            now.UtcDateTime,
            expires.UtcDateTime,
            signingCredentials);
        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
