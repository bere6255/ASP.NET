namespace PaymentsApi.Config;

public sealed class RateLimitingOptions
{
    public int AuthPermitLimit { get; init; } = 10;
    public int TransferPermitLimit { get; init; } = 30;
    public int WindowMinutes { get; init; } = 1;
}
