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

public sealed class AdminWizardPreflightRunsControllerTests
{
    [Fact]
    public async Task Create_StoresRunAndReturnsCreatedResult()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var audit = new RecordingAuditService();
        var controller = CreateController(fixture.DbContext, audit);

        var response = await controller.Create(
            new WizardPreflightRunCreateRequest(
            [
                new WizardPreflightCheckDto("api-health", "API health", "passed", "OK"),
                new WizardPreflightCheckDto("storage", "Storage probe", "failed", "timeout")
            ]),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var payload = Assert.IsType<WizardPreflightRunDto>(created.Value);
        Assert.Equal(2, payload.TotalCount);
        Assert.Equal(1, payload.PassedCount);
        Assert.Equal("qa-admin", payload.Actor);
        Assert.Equal(2, payload.Checks.Count);

        var row = await fixture.DbContext.WizardPreflightRuns.SingleAsync();
        Assert.Equal("qa-admin", row.Actor);
        Assert.Equal(2, row.TotalCount);
        Assert.Equal(1, row.PassedCount);
        Assert.Single(audit.Entries);
        Assert.Equal("wizard.preflight.run", audit.Entries[0].Action);
    }

    [Fact]
    public async Task Create_WithNoChecks_ReturnsBadRequest()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var controller = CreateController(fixture.DbContext, new RecordingAuditService());

        var response = await controller.Create(
            new WizardPreflightRunCreateRequest([]),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Equal(0, await fixture.DbContext.WizardPreflightRuns.CountAsync());
    }

    [Fact]
    public async Task Create_TrimsHistoryToConfiguredMaximum()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var now = DateTime.UtcNow.AddHours(-1);
        var seeded = Enumerable.Range(0, 200)
            .Select(i => new WizardPreflightRun
            {
                Actor = "seed",
                PassedCount = 1,
                TotalCount = 1,
                ChecksJson = "[{\"id\":\"seed\",\"label\":\"Seed\",\"status\":\"passed\",\"message\":\"ok\"}]",
                RanAtUtc = now.AddMinutes(i)
            })
            .ToList();
        fixture.DbContext.WizardPreflightRuns.AddRange(seeded);
        await fixture.DbContext.SaveChangesAsync();

        var controller = CreateController(fixture.DbContext, new RecordingAuditService());
        var response = await controller.Create(
            new WizardPreflightRunCreateRequest(
            [
                new WizardPreflightCheckDto("auth", "Auth probe", "passed", "ok")
            ]),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var payload = Assert.IsType<WizardPreflightRunDto>(created.Value);
        Assert.Equal(200, await fixture.DbContext.WizardPreflightRuns.CountAsync());
        Assert.True(await fixture.DbContext.WizardPreflightRuns.AnyAsync(x => x.Id == payload.Id));
    }

    [Fact]
    public async Task Get_RespectsLimitAndReturnsNewestFirst()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.WizardPreflightRuns.AddRange(
            new WizardPreflightRun
            {
                Actor = "a",
                PassedCount = 1,
                TotalCount = 1,
                ChecksJson = "[{\"id\":\"one\",\"label\":\"One\",\"status\":\"passed\",\"message\":\"ok\"}]",
                RanAtUtc = DateTime.UtcNow.AddMinutes(-2)
            },
            new WizardPreflightRun
            {
                Actor = "b",
                PassedCount = 0,
                TotalCount = 1,
                ChecksJson = "invalid-json",
                RanAtUtc = DateTime.UtcNow.AddMinutes(-1)
            });
        await fixture.DbContext.SaveChangesAsync();

        var controller = CreateController(fixture.DbContext, new RecordingAuditService());
        var response = await controller.Get(limit: 1, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<List<WizardPreflightRunDto>>(ok.Value);

        Assert.Single(payload);
        Assert.Equal("b", payload[0].Actor);
        Assert.Empty(payload[0].Checks);
    }

    private static AdminWizardPreflightRunsController CreateController(AppDbContext dbContext, IAdminAuditService audit)
    {
        var controller = new AdminWizardPreflightRunsController(dbContext, audit);
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
        public List<(string Action, string Actor, string EntityType, string EntityId)> Entries { get; } = [];

        public Task WriteAsync(
            string action,
            string actor,
            string entityType,
            string entityId,
            object? details = null,
            CancellationToken cancellationToken = default)
        {
            Entries.Add((action, actor, entityType, entityId));
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
