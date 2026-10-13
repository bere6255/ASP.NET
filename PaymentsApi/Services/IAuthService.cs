using PaymentsApi.Models.DTOs;
using PaymentsApi.Models.Responses;

namespace PaymentsApi.Services;

public interface IAuthService
{
    Task<AuthTokens> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthTokens> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthTokens> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);
}
