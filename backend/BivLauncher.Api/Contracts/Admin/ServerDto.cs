namespace BivLauncher.Api.Contracts.Admin;

public sealed record ServerDto(
    Guid Id,
    Guid ProfileId,
    string Name,
    string Address,
    int Port,
    string MainJarPath,
    string RuProxyAddress,
    int RuProxyPort,
    string RuJarPath,
    string IconKey,
    string LoaderType,
    string McVersion,
    string BuildId,
    bool Enabled,
    int Order);
