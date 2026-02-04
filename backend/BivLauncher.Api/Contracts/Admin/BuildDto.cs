namespace BivLauncher.Api.Contracts.Admin;

public sealed record BuildDto(
    Guid Id,
    Guid ProfileId,
    string LoaderType,
    string McVersion,
    DateTime CreatedAtUtc,
    string Status,
    string ManifestKey,
    string ClientVersion,
    string ErrorMessage,
    int FilesCount,
    long TotalSizeBytes);
