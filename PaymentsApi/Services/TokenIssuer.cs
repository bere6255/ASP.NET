using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PaymentsApi.Config;
using PaymentsApi.Models.Entities;
using PaymentsApi.Models.Responses;

namespace PaymentsApi.Services;

public sealed class TokenIssuer(IOptions<JwtOptions> options) : ITokenIssuer
{
    public AuthTokens Issue(User user, string refreshToken)
    {
        var settings = options.Value;
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.NormalizedEmail),
            new Claim(ClaimTypes.Role, user.Role)
        };
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(settings.Issuer, settings.Audience, claims, now.UtcDateTime,
            expires.UtcDateTime, credentials);
        return new AuthTokens(new JwtSecurityTokenHandler().WriteToken(jwt), refreshToken, expires);
    }
}
