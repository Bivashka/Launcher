namespace BivLauncher.Api.Contracts.Admin;

public sealed record StorageTestResultDto(
    bool Success,
    bool UseS3,
    string Message,
    string ProbeKey,
    long RoundTripMs,
    DateTime TestedAtUtc);
