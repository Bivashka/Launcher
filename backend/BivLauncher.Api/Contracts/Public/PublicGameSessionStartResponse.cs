namespace BivLauncher.Api.Contracts.Public;

public sealed record PublicGameSessionStartResponse(
    Guid SessionId,
    int HeartbeatIntervalSeconds,
    int ExpiresAfterSeconds,
    int ActiveAccountsOnDevice,
    int Limit);
