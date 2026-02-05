using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/docs")]
public sealed partial class AdminDocumentationController(
    AppDbContext dbContext,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DocumentationArticleDto>>> List(
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] bool publishedOnly = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedCategory = NormalizeCategory(category);
        var normalizedSearch = (search ?? string.Empty).Trim();

        var query = dbContext.DocumentationArticles.AsNoTracking().AsQueryable();
        if (publishedOnly)
        {
            query = query.Where(x => x.Published);
        }

        if (!string.IsNullOrWhiteSpace(normalizedCategory))
        {
            query = query.Where(x => x.Category == normalizedCategory);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(x =>
                x.Title.Contains(normalizedSearch) ||
                x.Summary.Contains(normalizedSearch) ||
                x.BodyMarkdown.Contains(normalizedSearch));
        }

        var items = await query
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Order)
            .ThenBy(x => x.Title)
            .Select(x => new DocumentationArticleDto(
                x.Id,
                x.Slug,
                x.Title,
                x.Category,
                x.Summary,
                x.BodyMarkdown,
                x.Order,
                x.Published,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentationArticleDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.DocumentationArticles
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new DocumentationArticleDto(
                x.Id,
                x.Slug,
                x.Title,
                x.Category,
                x.Summary,
                x.BodyMarkdown,
                x.Order,
                x.Published,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<DocumentationArticleDto>> Create(
        [FromBody] DocumentationArticleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeRequest(request, out var normalized, out var error))
        {
            return BadRequest(new { error });
        }

        var slugInUse = await dbContext.DocumentationArticles
            .AnyAsync(x => x.Slug == normalized.Slug, cancellationToken);
        if (slugInUse)
        {
            return Conflict(new { error = "Slug is already in use." });
        }

        var entity = new DocumentationArticle
        {
            Slug = normalized.Slug,
            Title = normalized.Title,
            Category = normalized.Category,
            Summary = normalized.Summary,
            BodyMarkdown = normalized.BodyMarkdown,
            Order = normalized.Order,
            Published = normalized.Published,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        dbContext.DocumentationArticles.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "docs.create",
            actor: actor,
            entityType: "docs",
            entityId: entity.Slug,
            details: new
            {
                entity.Id,
                entity.Category,
                entity.Published
            },
            cancellationToken: cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, Map(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DocumentationArticleDto>> Update(
        Guid id,
        [FromBody] DocumentationArticleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeRequest(request, out var normalized, out var error))
        {
            return BadRequest(new { error });
        }

        var entity = await dbContext.DocumentationArticles.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var slugInUse = await dbContext.DocumentationArticles
            .AnyAsync(x => x.Slug == normalized.Slug && x.Id != id, cancellationToken);
        if (slugInUse)
        {
            return Conflict(new { error = "Slug is already in use." });
        }

        entity.Slug = normalized.Slug;
        entity.Title = normalized.Title;
        entity.Category = normalized.Category;
        entity.Summary = normalized.Summary;
        entity.BodyMarkdown = normalized.BodyMarkdown;
        entity.Order = normalized.Order;
        entity.Published = normalized.Published;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "docs.update",
            actor: actor,
            entityType: "docs",
            entityId: entity.Slug,
            details: new
            {
                entity.Id,
                entity.Category,
                entity.Published
            },
            cancellationToken: cancellationToken);

        return Ok(Map(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.DocumentationArticles.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        dbContext.DocumentationArticles.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "docs.delete",
            actor: actor,
            entityType: "docs",
            entityId: entity.Slug,
            details: new
            {
                entity.Id,
                entity.Title
            },
            cancellationToken: cancellationToken);

        return NoContent();
    }

    [HttpPost("seed")]
    public async Task<ActionResult<DocumentationSeedResponse>> Seed(CancellationToken cancellationToken)
    {
        var templates = BuildSeedTemplates();
        var existing = await dbContext.DocumentationArticles
            .AsNoTracking()
            .Select(x => x.Slug)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var inserted = 0;
        var skipped = 0;
        var affectedSlugs = new List<string>();
        foreach (var template in templates)
        {
            affectedSlugs.Add(template.Slug);
            if (existingSet.Contains(template.Slug))
            {
                skipped++;
                continue;
            }

            dbContext.DocumentationArticles.Add(new DocumentationArticle
            {
                Slug = template.Slug,
                Title = template.Title,
                Category = template.Category,
                Summary = template.Summary,
                BodyMarkdown = template.BodyMarkdown,
                Order = template.Order,
                Published = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            inserted++;
        }

        if (inserted > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "docs.seed",
            actor: actor,
            entityType: "docs",
            entityId: "seed",
            details: new { inserted, skipped },
            cancellationToken: cancellationToken);

        return Ok(new DocumentationSeedResponse(inserted, skipped, affectedSlugs));
    }

    private static bool TryNormalizeRequest(
        DocumentationArticleUpsertRequest request,
        out DocumentationArticleUpsertRequest normalized,
        out string error)
    {
        var slug = NormalizeSlug(request.Slug);
        if (string.IsNullOrWhiteSpace(slug))
        {
            normalized = request;
            error = "Slug is required and must contain only lowercase latin letters, numbers and '-'.";
            return false;
        }

        var title = request.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            normalized = request;
            error = "Title is required.";
            return false;
        }

        var body = request.BodyMarkdown.Replace("\r\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            normalized = request;
            error = "BodyMarkdown is required.";
            return false;
        }

        normalized = new DocumentationArticleUpsertRequest
        {
            Slug = slug,
            Title = title,
            Category = NormalizeCategory(request.Category),
            Summary = request.Summary.Trim(),
            BodyMarkdown = body,
            Order = Math.Clamp(request.Order, 0, 10000),
            Published = request.Published
        };

        error = string.Empty;
        return true;
    }

    private static string NormalizeSlug(string rawSlug)
    {
        var normalized = (rawSlug ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is < 2 or > 96)
        {
            return string.Empty;
        }

        return SlugRegex().IsMatch(normalized) ? normalized : string.Empty;
    }

    private static string NormalizeCategory(string? category)
    {
        var normalized = (category ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "docs";
        }

        return CategoryRegex().IsMatch(normalized) ? normalized : "docs";
    }

    private static DocumentationArticleDto Map(DocumentationArticle x)
    {
        return new DocumentationArticleDto(
            x.Id,
            x.Slug,
            x.Title,
            x.Category,
            x.Summary,
            x.BodyMarkdown,
            x.Order,
            x.Published,
            x.CreatedAtUtc,
            x.UpdatedAtUtc);
    }

    private static IReadOnlyList<DocumentationArticleUpsertRequest> BuildSeedTemplates()
    {
        return
        [
            new DocumentationArticleUpsertRequest
            {
                Slug = "install-linux-docker",
                Title = "Установка на Linux (Docker)",
                Category = "installation",
                Summary = "Быстрый старт: установка, первый вход в админку, проверка API.",
                Order = 10,
                BodyMarkdown = """
# Установка BivLauncher (Linux + Docker)

1. Скопируйте `.env.example` в `.env`.
2. Запустите установщик: `bash deploy/installer.sh`.
3. После запуска откройте админку и создайте первого администратора.

## Проверки
- `GET /health` возвращает `ok`
- `GET /api/public/bootstrap` доступен

## Частые ошибки
- Заняты порты: измените `API_PORT` / `ADMIN_PORT` в `.env`.
- Нет доступа к Docker: добавьте пользователя в группу `docker`.
"""
            },
            new DocumentationArticleUpsertRequest
            {
                Slug = "update-and-migrations",
                Title = "Обновление и миграции",
                Category = "operations",
                Summary = "Как обновлять backend/admin/launcher без потери данных.",
                Order = 20,
                BodyMarkdown = """
# Обновление платформы

## Рекомендованный порядок
1. Сделать backup БД и `.env`.
2. Обновить образы/код.
3. Запустить `docker compose up -d --build`.
4. Проверить `/health` и критичные сценарии логина/запуска.

## Важно
- Миграции БД применяются автоматически при старте API.
- После обновления проверяйте раздел Audit/Crashes.
"""
            },
            new DocumentationArticleUpsertRequest
            {
                Slug = "backup-and-restore",
                Title = "Backup и восстановление",
                Category = "operations",
                Summary = "Минимальный набор резервных копий и процесс восстановления.",
                Order = 30,
                BodyMarkdown = """
# Backup и Restore

## Что бэкапить
- PostgreSQL database
- `.env`
- объектное хранилище (если используется S3/MinIO)

## Минимальная политика
- Daily backup БД
- Проверка восстановления не реже 1 раза в месяц

## Restore (кратко)
1. Остановить запись в систему
2. Восстановить БД
3. Проверить доступность артефактов
4. Выполнить smoke-тесты
"""
            },
            new DocumentationArticleUpsertRequest
            {
                Slug = "auth-modes-and-fields",
                Title = "Режимы авторизации и поля login/password",
                Category = "faq",
                Summary = "External/ANY режимы и кастомизация ключей запроса.",
                Order = 40,
                BodyMarkdown = """
# Авторизация: External и ANY

## External
- Лаунчер отправляет запрос во внешний auth endpoint.
- В админке настраиваются ключи полей: `loginFieldKey`, `passwordFieldKey`.

## ANY
- Принимается любой login/password.
- Используйте только в тестовой/закрытой среде.

## FAQ
**Почему логин не работает?**
- Проверьте URL auth-провайдера.
- Проверьте соответствие ключей полей API провайдера.
"""
            },
            new DocumentationArticleUpsertRequest
            {
                Slug = "crash-logs-triage",
                Title = "Crash-логи: как разбирать",
                Category = "faq",
                Summary = "Где смотреть краши, как менять статус, как экспортировать.",
                Order = 50,
                BodyMarkdown = """
# Разбор crash-логов

## Поток
1. Лаунчер отправляет crash report на backend.
2. В админке в разделе Crashes появляется запись со статусом `new`.
3. После разбора переведите в `resolved`.

## На что смотреть в первую очередь
- `Reason`
- `ErrorType`
- `ExitCode`
- `JavaVersion`
- `LogExcerpt`
"""
            }
        ];
    }

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.Compiled)]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.Compiled)]
    private static partial Regex CategoryRegex();
}
