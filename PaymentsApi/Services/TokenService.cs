using System.Security.Cryptography;
using System.Text;

namespace PaymentsApi.Services;

public sealed class TokenService : ITokenService
{
    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
