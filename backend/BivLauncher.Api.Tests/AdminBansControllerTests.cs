using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Controllers;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace BivLauncher.Api.Tests;

public sealed class AdminBansControllerTests
{
    [Fact]
    public async Task BanAccount_WhenLatestSessionContainsDeviceIdentity_UsesSessionValues()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var now = DateTime.UtcNow;

        var account = new AuthAccount
        {
            Username = "PlayerOne",
            ExternalId = "player-one-id",
            Roles = "player",
            HwidHash = string.Empty,
            DeviceUserName = string.Empty,
            UpdatedAtUtc = now.AddMinutes(-10)
        };
        fixture.DbContext.AuthAccounts.Add(account);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.ActiveGameSessions.Add(new ActiveGameSession
        {
            AccountId = account.Id,
            Username = account.Username,
            HwidHash = "fresh-hwid",
            DeviceUserName = "gaming-pc",
            ServerName = "Main",
            StartedAtUtc = now.AddMinutes(-5),
            LastHeartbeatAtUtc = now.AddSeconds(-20),
            ExpiresAtUtc = now.AddMinutes(2)
        });
        await fixture.DbContext.SaveChangesAsync();

        var controller = CreateController(fixture.DbContext, new RecordingAuditService());
        var response = await controller.BanAccount(
            "playerone",
            new AccountBanCreateRequest { Reason = "manual review" },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<BanDto>(ok.Value);

        Assert.Equal(account.Id, payload.AccountId);
        Assert.Equal("fresh-hwid", payload.HwidHash);
        Assert.Equal("gaming-pc", payload.DeviceUserName);

        var storedAccount = await fixture.DbContext.AuthAccounts.SingleAsync();
        Assert.Equal("fresh-hwid", storedAccount.HwidHash);
        Assert.Equal("gaming-pc", storedAccount.DeviceUserName);
        Assert.True(storedAccount.Banned);

        var storedBan = await fixture.DbContext.HardwareBans.SingleAsync();
        Assert.Equal(account.Id, storedBan.AccountId);
        Assert.Equal("fresh-hwid", storedBan.HwidHash);
        Assert.Equal("gaming-pc", storedBan.DeviceUserName);
    }

    [Fact]
    public async Task BanAccount_WhenDirectLookupMisses_ResolvesThroughLatestSessionUsername()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var now = DateTime.UtcNow;

        var account = new AuthAccount
        {
            Username = "LegacyName",
            ExternalId = "player-two-id",
            Roles = "player",
            UpdatedAtUtc = now.AddHours(-1)
        };
        fixture.DbContext.AuthAccounts.Add(account);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.ActiveGameSessions.Add(new ActiveGameSession
        {
            AccountId = account.Id,
            Username = "FreshName",
            HwidHash = "hwid-42",
            DeviceUserName = "pc-42",
            ServerName = "Main",
            StartedAtUtc = now.AddMinutes(-4),
            LastHeartbeatAtUtc = now.AddSeconds(-15),
            ExpiresAtUtc = now.AddMinutes(2)
        });
        await fixture.DbContext.SaveChangesAsync();

        var controller = CreateController(fixture.DbContext, new RecordingAuditService());
        var response = await controller.BanAccount(
            "freshname",
            new AccountBanCreateRequest { Reason = "session fallback" },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<BanDto>(ok.Value);

        Assert.Equal(account.Id, payload.AccountId);
        Assert.Equal("hwid-42", payload.HwidHash);
        Assert.Equal("pc-42", payload.DeviceUserName);
    }

    private static AdminBansController CreateController(AppDbContext dbContext, IAdminAuditService audit)
    {
        var controller = new AdminBansController(dbContext, audit);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "qa-admin"),
                new Claim(ClaimTypes.Role, "admin")
            ], "TestAuth"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private sealed class RecordingAuditService : IAdminAuditService
    {
        public Task WriteAsync(
            string action,
            string actor,
            string entityType,
            string entityId,
            object? details = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(SqliteConnection connection, AppDbContext dbContext)
        {
            Connection = connection;
            DbContext = dbContext;
        }

        public SqliteConnection Connection { get; }
        public AppDbContext DbContext { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            return new TestFixture(connection, dbContext);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
