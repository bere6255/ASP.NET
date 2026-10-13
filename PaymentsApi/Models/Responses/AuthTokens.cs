namespace PaymentsApi.Models.Responses;

public sealed record AuthTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
