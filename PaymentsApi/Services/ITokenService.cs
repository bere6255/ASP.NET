namespace PaymentsApi.Services;

public interface ITokenService
{
    string CreateRefreshToken();
    string Hash(string token);
}
