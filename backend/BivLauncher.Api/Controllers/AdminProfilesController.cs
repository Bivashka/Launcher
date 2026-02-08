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
[Route("api/admin/profiles")]
public sealed class AdminProfilesController(
    AppDbContext dbContext,
    IBuildPipelineService buildPipelineService,
    IBuildSourcesLayoutService buildSourcesLayoutService,
    IAdminAuditService auditService) : ControllerBase
{
    private static readonly Regex SlugPattern = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProfileDto>>> List(CancellationToken cancellationToken)
    {
        var profiles = await dbContext.Profiles
            .AsNoTracking()
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Name)
            .Select(x => new ProfileDto(
                x.Id,
                x.Name,
                x.Slug,
                x.Description,
                x.Enabled,
                x.IconKey,
                x.Priority,
                x.RecommendedRamMb,
                x.JvmArgsDefault,
                x.GameArgsDefault,
                x.BundledJavaPath,
                x.BundledRuntimeKey,
                x.BundledRuntimeSha256,
                x.BundledRuntimeSizeBytes,
                x.BundledRuntimeContentType,
                x.LatestBuildId,
                x.LatestManifestKey,
                x.LatestClientVersion,
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(profiles);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProfileDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var profile = await dbContext.Profiles
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ProfileDto(
                x.Id,
                x.Name,
                x.Slug,
                x.Description,
                x.Enabled,
                x.IconKey,
                x.Priority,
                x.RecommendedRamMb,
                x.JvmArgsDefault,
                x.GameArgsDefault,
                x.BundledJavaPath,
                x.BundledRuntimeKey,
                x.BundledRuntimeSha256,
                x.BundledRuntimeSizeBytes,
                x.BundledRuntimeContentType,
                x.LatestBuildId,
                x.LatestManifestKey,
                x.LatestClientVersion,
                x.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpPost]
    public async Task<ActionResult<ProfileDto>> Create([FromBody] ProfileUpsertRequest request, CancellationToken cancellationToken)
    {
        var normalizedSlug = NormalizeSlug(request.Slug);
        var normalizedRuntimeKey = NormalizeBundledRuntimeKey(request.BundledRuntimeKey);
        if (!TryValidateLaunchArgs(request.JvmArgsDefault, out var jvmError))
        {
            return BadRequest(new { error = $"JvmArgsDefault: {jvmError}" });
        }
        if (!TryValidateLaunchArgs(request.GameArgsDefault, out var gameError))
        {
            return BadRequest(new { error = $"GameArgsDefault: {gameError}" });
        }
        if (!IsSlugValid(normalizedSlug))
        {
            return BadRequest(new { error = "Slug must contain only lowercase latin letters, numbers, and '-'." });
        }

        var slugInUse = await dbContext.Profiles.AnyAsync(x => x.Slug == normalizedSlug, cancellationToken);
        if (slugInUse)
        {
            return Conflict(new { error = "Slug is already in use." });
        }

        try
        {
            buildSourcesLayoutService.EnsureProfileLayout(normalizedSlug);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = $"Failed to initialize profile build-sources layout: {ex.Message}"
            });
        }

        var profile = new Profile
        {
            Name = request.Name.Trim(),
            Slug = normalizedSlug,
            Description = request.Description.Trim(),
            Enabled = request.Enabled,
            IconKey = request.IconKey.Trim(),
            Priority = request.Priority,
            RecommendedRamMb = request.RecommendedRamMb,
            JvmArgsDefault = NormalizeLaunchArgs(request.JvmArgsDefault),
            GameArgsDefault = NormalizeLaunchArgs(request.GameArgsDefault),
            BundledJavaPath = NormalizeBundledJavaPath(request.BundledJavaPath),
            BundledRuntimeKey = normalizedRuntimeKey
        };

        dbContext.Profiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "profile.create",
            actor: actor,
            entityType: "profile",
            entityId: profile.Slug,
            details: new
            {
                profileId = profile.Id,
                profile.Enabled,
                profile.Priority
            },
            cancellationToken: cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, MapProfile(profile));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProfileDto>> Update(Guid id, [FromBody] ProfileUpsertRequest request, CancellationToken cancellationToken)
    {
        var profile = await dbContext.Profiles.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        var normalizedSlug = NormalizeSlug(request.Slug);
        var normalizedRuntimeKey = NormalizeBundledRuntimeKey(request.BundledRuntimeKey);
        if (!TryValidateLaunchArgs(request.JvmArgsDefault, out var jvmError))
        {
            return BadRequest(new { error = $"JvmArgsDefault: {jvmError}" });
        }
        if (!TryValidateLaunchArgs(request.GameArgsDefault, out var gameError))
        {
            return BadRequest(new { error = $"GameArgsDefault: {gameError}" });
        }
        if (!IsSlugValid(normalizedSlug))
        {
            return BadRequest(new { error = "Slug must contain only lowercase latin letters, numbers, and '-'." });
        }

        var slugInUse = await dbContext.Profiles.AnyAsync(x => x.Slug == normalizedSlug && x.Id != id, cancellationToken);
        if (slugInUse)
        {
            return Conflict(new { error = "Slug is already in use." });
        }

        var previousSlug = profile.Slug;
        try
        {
            buildSourcesLayoutService.EnsureProfileLayout(normalizedSlug, previousSlug);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = $"Failed to initialize profile build-sources layout: {ex.Message}"
            });
        }

        profile.Name = request.Name.Trim();
        profile.Slug = normalizedSlug;
        profile.Description = request.Description.Trim();
        profile.Enabled = request.Enabled;
        profile.IconKey = request.IconKey.Trim();
        profile.Priority = request.Priority;
        profile.RecommendedRamMb = request.RecommendedRamMb;
        profile.JvmArgsDefault = NormalizeLaunchArgs(request.JvmArgsDefault);
        profile.GameArgsDefault = NormalizeLaunchArgs(request.GameArgsDefault);
        profile.BundledJavaPath = NormalizeBundledJavaPath(request.BundledJavaPath);
        if (!string.Equals(profile.BundledRuntimeKey, normalizedRuntimeKey, StringComparison.Ordinal))
        {
            profile.BundledRuntimeSha256 = string.Empty;
            profile.BundledRuntimeSizeBytes = 0;
            profile.BundledRuntimeContentType = string.Empty;
        }

        profile.BundledRuntimeKey = normalizedRuntimeKey;

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "profile.update",
            actor: actor,
            entityType: "profile",
            entityId: profile.Slug,
            details: new
            {
                profileId = profile.Id,
                profile.Enabled,
                profile.Priority
            },
            cancellationToken: cancellationToken);

        return Ok(MapProfile(profile));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var profile = await dbContext.Profiles.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        dbContext.Profiles.Remove(profile);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "profile.delete",
            actor: actor,
            entityType: "profile",
            entityId: profile.Slug,
            details: new
            {
                profileId = profile.Id,
                profile.Name
            },
            cancellationToken: cancellationToken);

        return NoContent();
    }

    [HttpGet("{id:guid}/builds")]
    public async Task<ActionResult<IReadOnlyList<BuildDto>>> GetBuilds(Guid id, CancellationToken cancellationToken)
    {
        var profileExists = await dbContext.Profiles.AnyAsync(x => x.Id == id, cancellationToken);
        if (!profileExists)
        {
            return NotFound();
        }

        var builds = await dbContext.Builds
            .AsNoTracking()
            .Where(x => x.ProfileId == id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(20)
            .Select(x => new BuildDto(
                x.Id,
                x.ProfileId,
                x.LoaderType,
                x.McVersion,
                x.CreatedAtUtc,
                x.Status,
                x.ManifestKey,
                x.ClientVersion,
                x.ErrorMessage,
                x.FilesCount,
                x.TotalSizeBytes))
            .ToListAsync(cancellationToken);

        return Ok(builds);
    }

    [HttpPost("{id:guid}/rebuild")]
    public async Task<ActionResult<BuildDto>> Rebuild(
        Guid id,
        [FromBody] ProfileRebuildRequest? request,
        CancellationToken cancellationToken)
    {
        var rebuildRequest = request ?? new ProfileRebuildRequest();
        if (!TryValidateLaunchArgs(rebuildRequest.JvmArgsDefault, out var jvmError))
        {
            return BadRequest(new { error = $"JvmArgsDefault: {jvmError}" });
        }
        if (!TryValidateLaunchArgs(rebuildRequest.GameArgsDefault, out var gameError))
        {
            return BadRequest(new { error = $"GameArgsDefault: {gameError}" });
        }

        try
        {
            var build = await buildPipelineService.RebuildProfileAsync(id, rebuildRequest, cancellationToken);
            var profile = await dbContext.Profiles
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            var actor = User.Identity?.Name ?? "admin";
            await auditService.WriteAsync(
                action: "profile.rebuild",
                actor: actor,
                entityType: "profile",
                entityId: profile?.Slug ?? id.ToString(),
                details: new
                {
                    profileId = id,
                    buildId = build.Id,
                    build.Status,
                    build.LoaderType,
                    build.McVersion
                },
                cancellationToken: cancellationToken);
            return Ok(build);
        }
        catch (KeyNotFoundException)
        {
            var actor = User.Identity?.Name ?? "admin";
            await auditService.WriteAsync(
                action: "profile.rebuild.failed",
                actor: actor,
                entityType: "profile",
                entityId: id.ToString(),
                details: new
                {
                    profileId = id,
                    error = "Profile not found."
                },
                cancellationToken: cancellationToken);
            return NotFound();
        }
        catch (DirectoryNotFoundException ex)
        {
            var actor = User.Identity?.Name ?? "admin";
            await auditService.WriteAsync(
                action: "profile.rebuild.failed",
                actor: actor,
                entityType: "profile",
                entityId: id.ToString(),
                details: new
                {
                    profileId = id,
                    error = ex.Message
                },
                cancellationToken: cancellationToken);
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            var actor = User.Identity?.Name ?? "admin";
            await auditService.WriteAsync(
                action: "profile.rebuild.failed",
                actor: actor,
                entityType: "profile",
                entityId: id.ToString(),
                details: new
                {
                    profileId = id,
                    error = ex.Message
                },
                cancellationToken: cancellationToken);
            return BadRequest(new { error = ex.Message });
        }
    }

    private static ProfileDto MapProfile(Profile profile)
    {
        return new ProfileDto(
            profile.Id,
            profile.Name,
            profile.Slug,
            profile.Description,
            profile.Enabled,
            profile.IconKey,
            profile.Priority,
            profile.RecommendedRamMb,
            profile.JvmArgsDefault,
            profile.GameArgsDefault,
            profile.BundledJavaPath,
            profile.BundledRuntimeKey,
            profile.BundledRuntimeSha256,
            profile.BundledRuntimeSizeBytes,
            profile.BundledRuntimeContentType,
            profile.LatestBuildId,
            profile.LatestManifestKey,
            profile.LatestClientVersion,
            profile.CreatedAtUtc);
    }

    private static string NormalizeSlug(string slug)
    {
        return slug.Trim().ToLowerInvariant();
    }

    private static bool IsSlugValid(string slug)
    {
        return slug.Length >= 2 && SlugPattern.IsMatch(slug);
    }

    private static string NormalizeBundledJavaPath(string? bundledJavaPath)
    {
        return string.IsNullOrWhiteSpace(bundledJavaPath)
            ? string.Empty
            : bundledJavaPath.Trim().Replace('\\', '/');
    }

    private static string NormalizeBundledRuntimeKey(string? bundledRuntimeKey)
    {
        return string.IsNullOrWhiteSpace(bundledRuntimeKey)
            ? string.Empty
            : bundledRuntimeKey.Trim().Replace('\\', '/').TrimStart('/');
    }

    private static string NormalizeLaunchArgs(string? rawArgs)
    {
        if (string.IsNullOrWhiteSpace(rawArgs))
        {
            return string.Empty;
        }

        var normalized = rawArgs.Replace("\r\n", "\n").Trim();
        return normalized.Length <= 4096 ? normalized : normalized[..4096];
    }

    private static bool TryValidateLaunchArgs(string? rawArgs, out string error)
    {
        var normalized = rawArgs?.Trim() ?? string.Empty;
        if (normalized.Length > 4096)
        {
            error = "must be 4096 characters or less.";
            return false;
        }

        if (normalized.IndexOfAny([';', '&', '|', '`']) >= 0)
        {
            error = "contains forbidden separators (;, &, |, `).";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
