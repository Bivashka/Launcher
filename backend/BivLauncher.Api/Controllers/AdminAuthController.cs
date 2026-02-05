using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Infrastructure;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminAuthController(
    AppDbContext dbContext,
    PasswordHasher<AdminUser> passwordHasher,
    IJwtTokenService jwtTokenService,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet("setup/status")]
    public async Task<IActionResult> SetupStatus(CancellationToken cancellationToken)
    {
        var needsSetup = !await dbContext.AdminUsers.AnyAsync(cancellationToken);
        return Ok(new { needsSetup });
    }

    [HttpPost("setup")]
    [EnableRateLimiting(RateLimitPolicies.AdminAuthPolicy)]
    public async Task<IActionResult> Setup([FromBody] AdminSetupRequest request, CancellationToken cancellationToken)
    {
        var adminExists = await dbContext.AdminUsers.AnyAsync(cancellationToken);
        if (adminExists)
        {
            return Conflict(new { error = "Admin account is already configured." });
        }

        var normalizedUsername = request.Username.Trim();
        var admin = new AdminUser
        {
            Username = normalizedUsername,
            PasswordHash = string.Empty
        };

        admin.PasswordHash = passwordHasher.HashPassword(admin, request.Password);
        dbContext.AdminUsers.Add(admin);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            action: "admin.setup",
            actor: normalizedUsername,
            entityType: "admin",
            entityId: admin.Id.ToString(),
            details: new
            {
                admin.Username
            },
            cancellationToken: cancellationToken);

        return Ok(new { success = true, username = admin.Username });
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.AdminAuthPolicy)]
    public async Task<IActionResult> Login([FromBody] AdminLoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedUsername = request.Username.Trim();
        var admin = await dbContext.AdminUsers.FirstOrDefaultAsync(x => x.Username == normalizedUsername, cancellationToken);
        if (admin is null)
        {
            await auditService.WriteAsync(
                action: "admin.login.failed",
                actor: normalizedUsername,
                entityType: "admin",
                entityId: normalizedUsername,
                details: new { error = "Invalid credentials." },
                cancellationToken: cancellationToken);
            return Unauthorized(new { error = "Invalid credentials." });
        }

        var verification = passwordHasher.VerifyHashedPassword(admin, admin.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            await auditService.WriteAsync(
                action: "admin.login.failed",
                actor: normalizedUsername,
                entityType: "admin",
                entityId: admin.Id.ToString(),
                details: new { error = "Invalid credentials." },
                cancellationToken: cancellationToken);
            return Unauthorized(new { error = "Invalid credentials." });
        }

        var token = jwtTokenService.CreateAdminToken(admin);
        await auditService.WriteAsync(
            action: "admin.login",
            actor: admin.Username,
            entityType: "admin",
            entityId: admin.Id.ToString(),
            details: new { success = true },
            cancellationToken: cancellationToken);
        return Ok(new
        {
            token,
            tokenType = "Bearer",
            username = admin.Username
        });
    }

    [Authorize(Roles = "admin")]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            username = User.Identity?.Name
        });
    }
}
