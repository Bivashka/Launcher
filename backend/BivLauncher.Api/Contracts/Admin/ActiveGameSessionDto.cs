namespace BivLauncher.Api.Contracts.Admin;

public sealed record ActiveGameSessionDto(
    Guid Id,
    Guid AccountId,
    string Username,
    string HwidHash,
    string DeviceUserName,
    Guid? ServerId,
    string ServerName,
    DateTime StartedAtUtc,
    DateTime LastHeartbeatAtUtc,
    DateTime ExpiresAtUtc,
    bool Active);
