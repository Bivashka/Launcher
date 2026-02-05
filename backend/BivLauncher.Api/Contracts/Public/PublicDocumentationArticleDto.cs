namespace BivLauncher.Api.Contracts.Public;

public sealed record PublicDocumentationArticleDto(
    string Slug,
    string Title,
    string Category,
    string Summary,
    string BodyMarkdown,
    int Order,
    DateTime UpdatedAtUtc);
