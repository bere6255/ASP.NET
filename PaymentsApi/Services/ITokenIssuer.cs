using PaymentsApi.Models.Entities;
using PaymentsApi.Models.Responses;

namespace PaymentsApi.Services;

public interface ITokenIssuer
{
    AuthTokens Issue(User user, string refreshToken);
}
