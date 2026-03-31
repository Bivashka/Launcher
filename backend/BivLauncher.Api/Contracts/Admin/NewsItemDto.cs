namespace BivLauncher.Api.Contracts.Admin;

public sealed record NewsItemDto(
    Guid Id,
    string Title,
    string Body,
    string Source,
    string ScopeType,
    string ScopeId,
    string ScopeName,
    bool Pinned,
    bool Enabled,
    DateTime CreatedAtUtc);
