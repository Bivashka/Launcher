using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BivLauncher.Api.Services;

public sealed class NewsImportService(
    AppDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    INewsRetentionService newsRetentionService,
    ILogger<NewsImportService> logger) : INewsImportService
{
    private static readonly HashSet<string> AllowedTypes = ["rss", "json", "markdown", "telegram", "vk"];
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly INewsRetentionService _newsRetentionService = newsRetentionService;
    private readonly ILogger<NewsImportService> _logger = logger;

    public async Task<NewsSourcesSyncResponse> SyncAsync(Guid? sourceId, bool force = false, CancellationToken cancellationToken = default)
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
            var now = DateTime.UtcNow;
            try
            {
                var minIntervalMinutes = Math.Clamp(source.MinFetchIntervalMinutes, 1, 1440);
                if (!force &&
                    source.LastFetchAttemptAtUtc.HasValue &&
                    source.LastFetchAttemptAtUtc.Value.AddMinutes(minIntervalMinutes) > now)
                {
                    var nextAllowed = source.LastFetchAttemptAtUtc.Value.AddMinutes(minIntervalMinutes);
                    error = $"Rate-limited: next fetch at {nextAllowed:O}.";
                    source.LastSyncAtUtc = now;
                    source.LastSyncError = error;

                    results.Add(new NewsSourceSyncResultDto(
                        source.Id,
                        source.Name,
                        source.Type,
                        importedForSource,
                        error));
                    continue;
                }

                source.LastFetchAttemptAtUtc = now;
                var fetched = await FetchItemsAsync(source, cancellationToken);
                source.CacheEtag = Truncate(fetched.ETag, 512);
                source.CacheLastModified = Truncate(fetched.LastModified, 128);

                if (fetched.NotModified)
                {
                    source.LastSyncAtUtc = now;
                    source.LastSyncError = string.Empty;
                    results.Add(new NewsSourceSyncResultDto(
                        source.Id,
                        source.Name,
                        source.Type,
                        importedForSource,
                        string.Empty));
                    continue;
                }

                var importedItems = fetched.Items;
                var sourceLabel = BuildSourceLabel(source);

                var existingTitles = await _dbContext.NewsItems
                    .AsNoTracking()
                    .Where(x => x.Source == sourceLabel)
                    .Select(x => x.Title)
                    .ToListAsync(cancellationToken);

                var titleSet = existingTitles
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

                if (importedForSource > 0)
                {
                    source.LastContentChangeAtUtc = now;
                }

                source.LastSyncAtUtc = now;
                source.LastSyncError = string.Empty;
            }
            catch (Exception ex)
            {
                error = Truncate(ex.Message, 1024);
                source.LastSyncAtUtc = now;
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

    private async Task<FetchResult> FetchItemsAsync(NewsSourceConfig source, CancellationToken cancellationToken)
    {
        var type = source.Type.Trim().ToLowerInvariant();
        if (!AllowedTypes.Contains(type))
        {
            throw new InvalidOperationException($"Unsupported source type '{source.Type}'.");
        }

        var sourceUrl = source.Url.Trim();
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var parsedSourceUri))
        {
            throw new InvalidOperationException("News source URL is invalid.");
        }

        var uri = BuildFetchUri(type, parsedSourceUri);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (TryParseEtag(source.CacheEtag, out var etag) && etag is not null)
        {
            request.Headers.IfNoneMatch.Add(etag);
        }

        if (TryParseLastModified(source.CacheLastModified, out var lastModified))
        {
            request.Headers.IfModifiedSince = lastModified;
        }

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var responseEtag = response.Headers.ETag?.ToString() ?? source.CacheEtag;
        var responseLastModified = response.Content.Headers.LastModified?.ToString("R")
            ?? source.CacheLastModified;

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new FetchResult(
                Items: [],
                ETag: responseEtag,
                LastModified: responseLastModified,
                NotModified: true);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Source returned HTTP {(int)response.StatusCode}.");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new FetchResult(
                Items: [],
                ETag: responseEtag,
                LastModified: responseLastModified,
                NotModified: false);
        }

        var maxItems = Math.Clamp(source.MaxItems, 1, 20);
        var parsedItems = type switch
        {
            "rss" => ParseRss(content, maxItems),
            "json" => ParseJson(content, maxItems),
            "markdown" => ParseMarkdown(content, source.Name, maxItems),
            "telegram" => ParseTelegram(content, source.Name, maxItems),
            "vk" => ParseVk(content, source.Name, maxItems),
            _ => []
        };

        return new FetchResult(
            Items: parsedItems,
            ETag: responseEtag,
            LastModified: responseLastModified,
            NotModified: false);
    }

    private static Uri BuildFetchUri(string sourceType, Uri sourceUri)
    {
        if (sourceType != "telegram")
        {
            return sourceUri;
        }

        if (!string.Equals(sourceUri.Host, "t.me", StringComparison.OrdinalIgnoreCase))
        {
            return sourceUri;
        }

        var path = sourceUri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            return sourceUri;
        }

        if (path.StartsWith("s/", StringComparison.OrdinalIgnoreCase))
        {
            return sourceUri;
        }

        var withFeedPrefix = $"https://t.me/s/{path}";
        return Uri.TryCreate(withFeedPrefix, UriKind.Absolute, out var normalized)
            ? normalized
            : sourceUri;
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

    private static IReadOnlyList<ImportedNewsItem> ParseTelegram(string content, string sourceName, int maxItems)
    {
        var items = ParseTelegramHtml(content, maxItems);
        if (items.Count > 0)
        {
            return items;
        }

        return ParseUnknownSourceContent(content, sourceName, maxItems);
    }

    private static IReadOnlyList<ImportedNewsItem> ParseVk(string content, string sourceName, int maxItems)
    {
        var items = ParseVkHtml(content, maxItems);
        if (items.Count > 0)
        {
            return items;
        }

        return ParseUnknownSourceContent(content, sourceName, maxItems);
    }

    private static IReadOnlyList<ImportedNewsItem> ParseUnknownSourceContent(string content, string sourceName, int maxItems)
    {
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                return ParseJson(content, maxItems);
            }
            catch
            {
            }
        }

        if (trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            try
            {
                return ParseRss(content, maxItems);
            }
            catch
            {
                return ParseHtmlSummary(content, sourceName);
            }
        }

        return ParseMarkdown(content, sourceName, maxItems);
    }

    private static IReadOnlyList<ImportedNewsItem> ParseTelegramHtml(string content, int maxItems)
    {
        var bodyMatches = Regex.Matches(
            content,
            "<div[^>]*class=\"[^\"]*tgme_widget_message_text[^\"]*\"[^>]*>(?<body>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (bodyMatches.Count == 0)
        {
            return [];
        }

        var timeMatches = Regex.Matches(
            content,
            "<time[^>]*datetime=\"(?<datetime>[^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var result = new List<ImportedNewsItem>();
        for (var i = 0; i < bodyMatches.Count && result.Count < maxItems; i++)
        {
            var bodyHtml = bodyMatches[i].Groups["body"].Value;
            var body = HtmlToText(bodyHtml);
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            var title = BuildTitleFromBody(body, "Telegram");
            var publishedAt = i < timeMatches.Count
                ? TryParseDateTime(timeMatches[i].Groups["datetime"].Value)
                : null;
            result.Add(new ImportedNewsItem(title, body, publishedAt));
        }

        return result;
    }

    private static IReadOnlyList<ImportedNewsItem> ParseVkHtml(string content, int maxItems)
    {
        var result = new List<ImportedNewsItem>();

        var postMatches = Regex.Matches(
            content,
            "<div[^>]*class=\"[^\"]*wall_post_text[^\"]*\"[^>]*>(?<body>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match match in postMatches)
        {
            var body = HtmlToText(match.Groups["body"].Value);
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            var title = BuildTitleFromBody(body, "VK");
            result.Add(new ImportedNewsItem(title, body, null));
            if (result.Count >= maxItems)
            {
                return result;
            }
        }

        var ogTitle = ExtractMetaContent(content, "og:title");
        var ogDescription = ExtractMetaContent(content, "og:description");
        if (!string.IsNullOrWhiteSpace(ogTitle) || !string.IsNullOrWhiteSpace(ogDescription))
        {
            var title = string.IsNullOrWhiteSpace(ogTitle) ? "VK" : ogTitle.Trim();
            var body = string.IsNullOrWhiteSpace(ogDescription) ? title : ogDescription.Trim();
            result.Add(new ImportedNewsItem(title, body, null));
        }

        return result;
    }

    private static IReadOnlyList<ImportedNewsItem> ParseHtmlSummary(string content, string sourceName)
    {
        var title = ExtractMetaContent(content, "og:title");
        var description = ExtractMetaContent(content, "og:description");
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? sourceName : title.Trim();
        var normalizedBody = string.IsNullOrWhiteSpace(description) ? normalizedTitle : description.Trim();
        if (string.IsNullOrWhiteSpace(normalizedBody))
        {
            normalizedBody = sourceName;
        }

        return [new ImportedNewsItem(normalizedTitle, normalizedBody, null)];
    }

    private static string ExtractMetaContent(string html, string propertyName)
    {
        var pattern = $"<meta[^>]*(?:property|name)=\"{Regex.Escape(propertyName)}\"[^>]*content=\"(?<content>[^\"]+)\"";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["content"].Value).Trim() : string.Empty;
    }

    private static string HtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withNewLines = Regex.Replace(html, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withNewLines, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var normalizedWhitespace = Regex.Replace(decoded, @"[ \t]+", " ").Trim();
        return Regex.Replace(normalizedWhitespace, @"\n\s+", "\n");
    }

    private static string BuildTitleFromBody(string body, string fallback)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        var firstLine = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? body.Trim();

        if (firstLine.Length <= 96)
        {
            return firstLine;
        }

        return $"{firstLine[..96].TrimEnd()}...";
    }

    private static bool TryParseEtag(string? raw, out EntityTagHeaderValue? etag)
    {
        etag = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return EntityTagHeaderValue.TryParse(raw.Trim(), out etag);
    }

    private static bool TryParseLastModified(string? raw, out DateTimeOffset value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = default;
            return false;
        }

        return DateTimeOffset.TryParse(raw.Trim(), out value);
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

    private sealed record FetchResult(
        IReadOnlyList<ImportedNewsItem> Items,
        string ETag,
        string LastModified,
        bool NotModified);

    private sealed record ImportedNewsItem(string Title, string Body, DateTime? PublishedAtUtc);
}
