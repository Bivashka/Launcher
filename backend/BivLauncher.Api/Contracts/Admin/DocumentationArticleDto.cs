namespace BivLauncher.Api.Contracts.Admin;

public sealed record DocumentationArticleDto(
    Guid Id,
    string Slug,
    string Title,
    string Category,
    string Summary,
    string BodyMarkdown,
    int Order,
    bool Published,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
