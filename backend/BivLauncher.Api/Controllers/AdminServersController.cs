using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/servers")]
public sealed class AdminServersController(
    AppDbContext dbContext,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServerDto>>> List([FromQuery] Guid? profileId, CancellationToken cancellationToken)
    {
        var query = dbContext.Servers.AsNoTracking().AsQueryable();
        if (profileId.HasValue)
        {
            query = query.Where(x => x.ProfileId == profileId.Value);
        }

        var servers = await query
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Name)
            .Select(x => new ServerDto(
                x.Id,
                x.ProfileId,
                x.Name,
                x.Address,
                x.Port,
                x.MainJarPath,
                x.RuProxyAddress,
                x.RuProxyPort,
                x.RuJarPath,
                x.IconKey,
                x.LoaderType,
                x.McVersion,
                x.BuildId,
                x.Enabled,
                x.Order))
            .ToListAsync(cancellationToken);

        return Ok(servers);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ServerDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var server = await dbContext.Servers
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ServerDto(
                x.Id,
                x.ProfileId,
                x.Name,
                x.Address,
                x.Port,
                x.MainJarPath,
                x.RuProxyAddress,
                x.RuProxyPort,
                x.RuJarPath,
                x.IconKey,
                x.LoaderType,
                x.McVersion,
                x.BuildId,
                x.Enabled,
                x.Order))
            .FirstOrDefaultAsync(cancellationToken);

        return server is null ? NotFound() : Ok(server);
    }

    [HttpPost]
    public async Task<ActionResult<ServerDto>> Create([FromBody] ServerUpsertRequest request, CancellationToken cancellationToken)
    {
        var profileExists = await dbContext.Profiles.AnyAsync(x => x.Id == request.ProfileId, cancellationToken);
        if (!profileExists)
        {
            return BadRequest(new { error = "Profile does not exist." });
        }

        var normalizedLoaderType = LoaderCatalog.NormalizeLoader(request.LoaderType);
        if (!LoaderCatalog.IsSupported(normalizedLoaderType))
        {
            return BadRequest(new { error = $"Unsupported loader type '{request.LoaderType}'. Allowed: {string.Join(", ", LoaderCatalog.SupportedLoaders)}." });
        }

        var server = new Server
        {
            ProfileId = request.ProfileId,
            Name = request.Name.Trim(),
            Address = request.Address.Trim(),
            Port = request.Port,
            MainJarPath = request.MainJarPath.Trim(),
            RuProxyAddress = request.RuProxyAddress.Trim(),
            RuProxyPort = request.RuProxyPort,
            RuJarPath = request.RuJarPath.Trim(),
            IconKey = request.IconKey.Trim(),
            LoaderType = normalizedLoaderType,
            McVersion = request.McVersion.Trim(),
            BuildId = request.BuildId.Trim(),
            Enabled = request.Enabled,
            Order = request.Order
        };

        dbContext.Servers.Add(server);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "server.create",
            actor: actor,
            entityType: "server",
            entityId: server.Id.ToString(),
            details: new
            {
                server.ProfileId,
                server.Name,
                server.LoaderType,
                server.McVersion,
                server.Enabled
            },
            cancellationToken: cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = server.Id }, MapServer(server));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ServerDto>> Update(Guid id, [FromBody] ServerUpsertRequest request, CancellationToken cancellationToken)
    {
        var server = await dbContext.Servers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (server is null)
        {
            return NotFound();
        }

        var profileExists = await dbContext.Profiles.AnyAsync(x => x.Id == request.ProfileId, cancellationToken);
        if (!profileExists)
        {
            return BadRequest(new { error = "Profile does not exist." });
        }

        var normalizedLoaderType = LoaderCatalog.NormalizeLoader(request.LoaderType);
        if (!LoaderCatalog.IsSupported(normalizedLoaderType))
        {
            return BadRequest(new { error = $"Unsupported loader type '{request.LoaderType}'. Allowed: {string.Join(", ", LoaderCatalog.SupportedLoaders)}." });
        }

        server.ProfileId = request.ProfileId;
        server.Name = request.Name.Trim();
        server.Address = request.Address.Trim();
        server.Port = request.Port;
        server.MainJarPath = request.MainJarPath.Trim();
        server.RuProxyAddress = request.RuProxyAddress.Trim();
        server.RuProxyPort = request.RuProxyPort;
        server.RuJarPath = request.RuJarPath.Trim();
        server.IconKey = request.IconKey.Trim();
        server.LoaderType = normalizedLoaderType;
        server.McVersion = request.McVersion.Trim();
        server.BuildId = request.BuildId.Trim();
        server.Enabled = request.Enabled;
        server.Order = request.Order;

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "server.update",
            actor: actor,
            entityType: "server",
            entityId: server.Id.ToString(),
            details: new
            {
                server.ProfileId,
                server.Name,
                server.LoaderType,
                server.McVersion,
                server.Enabled
            },
            cancellationToken: cancellationToken);

        return Ok(MapServer(server));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var server = await dbContext.Servers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (server is null)
        {
            return NotFound();
        }

        dbContext.Servers.Remove(server);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "server.delete",
            actor: actor,
            entityType: "server",
            entityId: server.Id.ToString(),
            details: new
            {
                server.ProfileId,
                server.Name
            },
            cancellationToken: cancellationToken);

        return NoContent();
    }

    private static ServerDto MapServer(Server server)
    {
        return new ServerDto(
            server.Id,
            server.ProfileId,
            server.Name,
            server.Address,
            server.Port,
            server.MainJarPath,
            server.RuProxyAddress,
            server.RuProxyPort,
            server.RuJarPath,
            server.IconKey,
            server.LoaderType,
            server.McVersion,
            server.BuildId,
            server.Enabled,
            server.Order);
    }
}
