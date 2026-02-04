using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Xml.Linq;

namespace BivLauncher.Api.Services;

public sealed class NewsImportService(
    AppDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    INewsRetentionService newsRetentionService,
    ILogger<NewsImportService> logger) : INewsImportService
{
    private static readonly HashSet<string> AllowedTypes = ["rss", "json", "markdown"];
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly INewsRetentionService _newsRetentionService = newsRetentionService;
    private readonly ILogger<NewsImportService> _logger = logger;

    public async Task<NewsSourcesSyncResponse> SyncAsync(Guid? sourceId, CancellationToken cancellationToken = default)
    {
        IQueryable<NewsSourceConfig> query = _dbContext.NewsSourceConfigs;
        if (sourceId.HasValue)
        {
            query = query.Where(x => x.Id == sourceId.Value);
        }
        else
        {
            query = query.Where(x => x.Enabled);
        }

        var sources = await query
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var results = new List<NewsSourceSyncResultDto>(sources.Count);
        var totalImported = 0;

        foreach (var source in sources)
        {
            var importedForSource = 0;
            var error = string.Empty;
            try
            {
                var importedItems = await FetchItemsAsync(source, cancellationToken);
                var sourceLabel = BuildSourceLabel(source);

                var existingTitles = await _dbContext.NewsItems
                    .AsNoTracking()
                    .Where(x => x.Source == sourceLabel)
                    .Select(x => x.Title)
                    .ToListAsync(cancellationToken);

                var titleSet = existingTitles
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var now = DateTime.UtcNow;
                foreach (var importedItem in importedItems)
                {
                    var title = Truncate(importedItem.Title.Trim(), 256);
                    if (string.IsNullOrWhiteSpace(title) || titleSet.Contains(title))
                    {
                        continue;
                    }

                    var body = Truncate(importedItem.Body.Trim(), 8192);
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        body = title;
                    }

                    _dbContext.NewsItems.Add(new NewsItem
                    {
                        Title = title,
                        Body = body,
                        Source = sourceLabel,
                        Pinned = false,
                        Enabled = true,
                        CreatedAtUtc = NormalizeCreatedAt(importedItem.PublishedAtUtc, now)
                    });

                    titleSet.Add(title);
                    importedForSource++;
                    totalImported++;
                }

                source.LastSyncAtUtc = DateTime.UtcNow;
                source.LastSyncError = string.Empty;
            }
            catch (Exception ex)
            {
                error = Truncate(ex.Message, 1024);
                source.LastSyncAtUtc = DateTime.UtcNow;
                source.LastSyncError = error;
                _logger.LogWarning(ex, "News source sync failed for {SourceId} ({SourceName})", source.Id, source.Name);
            }

            results.Add(new NewsSourceSyncResultDto(
                source.Id,
                source.Name,
                source.Type,
                importedForSource,
                error));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _newsRetentionService.ApplyRetentionAsync(cancellationToken);

        return new NewsSourcesSyncResponse(
            SourcesProcessed: sources.Count,
            Imported: totalImported,
            Results: results);
    }

    private async Task<IReadOnlyList<ImportedNewsItem>> FetchItemsAsync(NewsSourceConfig source, CancellationToken cancellationToken)
    {
        var type = source.Type.Trim().ToLowerInvariant();
        if (!AllowedTypes.Contains(type))
        {
            throw new InvalidOperationException($"Unsupported source type '{source.Type}'.");
        }

        if (!Uri.TryCreate(source.Url.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("News source URL is invalid.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Source returned HTTP {(int)response.StatusCode}.");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var maxItems = Math.Clamp(source.MaxItems, 1, 20);
        return type switch
        {
            "rss" => ParseRss(content, maxItems),
            "json" => ParseJson(content, maxItems),
            "markdown" => ParseMarkdown(content, source.Name, maxItems),
            _ => []
        };
    }

    private static IReadOnlyList<ImportedNewsItem> ParseRss(string content, int maxItems)
    {
        var doc = XDocument.Parse(content, LoadOptions.None);
        var result = new List<ImportedNewsItem>();

        var nodes = doc.Descendants()
            .Where(x => string.Equals(x.Name.LocalName, "item", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase));

        foreach (var node in nodes)
        {
            var title = GetXmlValue(node, "title");
            var body = GetXmlValue(node, "description", "content", "summary");
            var publishedRaw = GetXmlValue(node, "pubDate", "published", "updated");
            DateTime? publishedAt = TryParseDateTime(publishedRaw);

            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                body = title;
            }

            result.Add(new ImportedNewsItem(title, body, publishedAt));
            if (result.Count >= maxItems)
            {
                break;
            }
        }

        return result;
    }

    private static IReadOnlyList<ImportedNewsItem> ParseJson(string content, int maxItems)
    {
        using var doc = JsonDocument.Parse(content);
        var result = new List<ImportedNewsItem>();

        IEnumerable<JsonElement> items = ExtractJsonItems(doc.RootElement);
        foreach (var item in items)
        {
            string? title;
            string? body;
            DateTime? publishedAt;

            if (item.ValueKind == JsonValueKind.String)
            {
                title = item.GetString();
                body = title;
                publishedAt = null;
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                title = GetJsonString(item, "title", "headline", "name");
                body = GetJsonString(item, "body", "content", "description", "text");
                publishedAt = TryParseDateTime(GetJsonString(item, "publishedAt", "createdAt", "date", "pubDate"));
            }
            else
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                body = title;
            }

            result.Add(new ImportedNewsItem(title, body, publishedAt));
            if (result.Count >= maxItems)
            {
                break;
            }
        }

        return result;
    }

    private static IReadOnlyList<ImportedNewsItem> ParseMarkdown(string content, string sourceName, int maxItems)
    {
        if (maxItems <= 0)
        {
            return [];
        }

        var lines = content
            .Split('\n', StringSplitOptions.None)
            .Select(x => x.Trim())
            .ToList();

        var heading = lines
            .FirstOrDefault(x => x.StartsWith('#'))
            ?.TrimStart('#', ' ', '\t');

        var title = string.IsNullOrWhiteSpace(heading)
            ? sourceName.Trim()
            : heading.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            title = "News";
        }

        return [new ImportedNewsItem(title, content.Trim(), null)];
    }

    private static IEnumerable<JsonElement> ExtractJsonItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToList();
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        foreach (var listName in new[] { "items", "news", "articles", "posts", "data" })
        {
            if (root.TryGetProperty(listName, out var items) && items.ValueKind == JsonValueKind.Array)
            {
                return items.EnumerateArray().ToList();
            }
        }

        return [root];
    }

    private static string? GetXmlValue(XElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            var element = parent.Elements().FirstOrDefault(x => string.Equals(x.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            if (element is null)
            {
                continue;
            }

            var value = element.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetJsonString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static DateTime? TryParseDateTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTime.TryParse(raw, out var parsed)
            ? parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime()
            : null;
    }

    private static DateTime NormalizeCreatedAt(DateTime? value, DateTime fallbackUtcNow)
    {
        if (!value.HasValue)
        {
            return fallbackUtcNow;
        }

        var utc = value.Value.Kind == DateTimeKind.Utc
            ? value.Value
            : value.Value.ToUniversalTime();

        var upper = fallbackUtcNow.AddMinutes(5);
        if (utc > upper)
        {
            return fallbackUtcNow;
        }

        return utc;
    }

    private static string BuildSourceLabel(NewsSourceConfig source)
    {
        return Truncate($"{source.Type.Trim().ToLowerInvariant()}:{source.Name.Trim()}", 256);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private sealed record ImportedNewsItem(string Title, string Body, DateTime? PublishedAtUtc);
}
