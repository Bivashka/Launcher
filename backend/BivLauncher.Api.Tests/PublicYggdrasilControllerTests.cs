using BivLauncher.Api.Controllers;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace BivLauncher.Api.Tests;

public sealed class PublicYggdrasilControllerTests
{
    [Fact]
    public async Task Join_ThenHasJoined_ReturnsProfilePayload()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var options = BuildJwtOptions();

        var account = new AuthAccount
        {
            Username = "Bivashka",
            ExternalId = "account-42",
            SessionVersion = 2,
            Roles = "player"
        };
        fixture.DbContext.AuthAccounts.Add(account);
        await fixture.DbContext.SaveChangesAsync();

        var jwtTokenService = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(options));
        var token = jwtTokenService.CreatePlayerToken(account, ["player"]);

        var controller = new PublicYggdrasilController(
            fixture.DbContext,
            BuildConfiguration(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PublicYggdrasilController>.Instance);

        var joinResult = await controller.Join(
            new PublicYggdrasilController.YggdrasilJoinRequest
            {
                AccessToken = $"token:{token}:ffffffffffffffffffffffffffffffff",
                SelectedProfile = string.Empty,
                ServerId = "server-hash-1"
            },
            CancellationToken.None);
        Assert.IsType<NoContentResult>(joinResult);

        var hasJoinedResult = await controller.HasJoined("Bivashka", "server-hash-1", CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(hasJoinedResult);
        var payload = JsonSerializer.SerializeToElement(ok.Value);

        Assert.True(payload.TryGetProperty("id", out var idNode));
        Assert.True(payload.TryGetProperty("name", out var nameNode));
        Assert.Equal("Bivashka", nameNode.GetString());
        Assert.NotNull(idNode.GetString());
        Assert.Equal(32, idNode.GetString()!.Length);
    }

    [Fact]
    public async Task Join_AcceptsLegacyTokenFormat_WhenProfileIdPrecedesJwt()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var options = BuildJwtOptions();

        var account = new AuthAccount
        {
            Username = "LegacyTokenUser",
            ExternalId = "legacy-token-42",
            SessionVersion = 3,
            Roles = "player"
        };
        fixture.DbContext.AuthAccounts.Add(account);
        await fixture.DbContext.SaveChangesAsync();

        var jwtTokenService = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(options));
        var token = jwtTokenService.CreatePlayerToken(account, ["player"]);

        var controller = new PublicYggdrasilController(
            fixture.DbContext,
            BuildConfiguration(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PublicYggdrasilController>.Instance);

        var joinResult = await controller.Join(
            new PublicYggdrasilController.YggdrasilJoinRequest
            {
                AccessToken = $"token:ffffffffffffffffffffffffffffffff:{token}",
                SelectedProfile = string.Empty,
                ServerId = "legacy-token-server"
            },
            CancellationToken.None);
        Assert.IsType<NoContentResult>(joinResult);

        var hasJoinedResult = await controller.HasJoined("LegacyTokenUser", "legacy-token-server", CancellationToken.None);
        Assert.IsType<OkObjectResult>(hasJoinedResult);
    }

    [Fact]
    public async Task HasJoined_CanBeCalledMultipleTimes_ForSameTicket()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var options = BuildJwtOptions();

        var account = new AuthAccount
        {
            Username = "RepeatUser",
            ExternalId = "repeat-42",
            SessionVersion = 0,
            Roles = "player"
        };
        fixture.DbContext.AuthAccounts.Add(account);
        await fixture.DbContext.SaveChangesAsync();

        var jwtTokenService = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(options));
        var token = jwtTokenService.CreatePlayerToken(account, ["player"]);

        var controller = new PublicYggdrasilController(
            fixture.DbContext,
            BuildConfiguration(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PublicYggdrasilController>.Instance);

        var joinResult = await controller.Join(
            new PublicYggdrasilController.YggdrasilJoinRequest
            {
                AccessToken = $"token:{token}:ffffffffffffffffffffffffffffffff",
                SelectedProfile = string.Empty,
                ServerId = "repeat-server"
            },
            CancellationToken.None);
        Assert.IsType<NoContentResult>(joinResult);

        var firstCheck = await controller.HasJoined("RepeatUser", "repeat-server", CancellationToken.None);
        var secondCheck = await controller.HasJoined("RepeatUser", "repeat-server", CancellationToken.None);

        Assert.IsType<OkObjectResult>(firstCheck);
        Assert.IsType<OkObjectResult>(secondCheck);
    }

    [Fact]
    public async Task Validate_WhenSessionVersionChanged_ReturnsForbidden()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var options = BuildJwtOptions();

        var account = new AuthAccount
        {
            Username = "Tester",
            ExternalId = "tester-id",
            SessionVersion = 0,
            Roles = "player"
        };
        fixture.DbContext.AuthAccounts.Add(account);
        await fixture.DbContext.SaveChangesAsync();

        var jwtTokenService = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(options));
        var token = jwtTokenService.CreatePlayerToken(account, ["player"]);

        account.SessionVersion = 1;
        fixture.DbContext.AuthAccounts.Update(account);
        await fixture.DbContext.SaveChangesAsync();

        var controller = new PublicYggdrasilController(
            fixture.DbContext,
            BuildConfiguration(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PublicYggdrasilController>.Instance);

        var response = await controller.Validate(
            new PublicYggdrasilController.YggdrasilAccessTokenRequest
            {
                AccessToken = token,
                ClientToken = Guid.NewGuid().ToString("N")
            },
            CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var payload = JsonSerializer.SerializeToElement(forbidden.Value);
        Assert.Equal("ForbiddenOperationException", payload.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Validate_WhenProofConfiguredAndTokenHasNoProofId_ReturnsForbidden()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var options = BuildJwtOptions();

        var account = new AuthAccount
        {
            Username = "ProofUser",
            ExternalId = "proof-user-id",
            SessionVersion = 0,
            Roles = "player"
        };
        fixture.DbContext.AuthAccounts.Add(account);
        await fixture.DbContext.SaveChangesAsync();

        var token = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(options))
            .CreatePlayerToken(account, ["player"], launcherVersion: "1.0.3");

        var controller = new PublicYggdrasilController(
            fixture.DbContext,
            BuildConfiguration(launcherProof: "proof-secret"),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PublicYggdrasilController>.Instance);

        var response = await controller.Validate(
            new PublicYggdrasilController.YggdrasilAccessTokenRequest
            {
                AccessToken = token,
                ClientToken = Guid.NewGuid().ToString("N")
            },
            CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var payload = JsonSerializer.SerializeToElement(forbidden.Value);
        Assert.Equal("ForbiddenOperationException", payload.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Invalidate_WhenTokenIsValid_RevokesSessionAndTokenBecomesForbidden()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var options = BuildJwtOptions();

        var account = new AuthAccount
        {
            Username = "InvalidateUser",
            ExternalId = "invalidate-user-id",
            SessionVersion = 0,
            Roles = "player"
        };
        fixture.DbContext.AuthAccounts.Add(account);
        await fixture.DbContext.SaveChangesAsync();

        var token = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(options))
            .CreatePlayerToken(account, ["player"]);

        var controller = new PublicYggdrasilController(
            fixture.DbContext,
            BuildConfiguration(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PublicYggdrasilController>.Instance);

        var invalidateResponse = await controller.Invalidate(
            new PublicYggdrasilController.YggdrasilAccessTokenRequest
            {
                AccessToken = token
            },
            CancellationToken.None);
        Assert.IsType<NoContentResult>(invalidateResponse);

        var updatedAccount = await fixture.DbContext.AuthAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == "InvalidateUser");
        Assert.NotNull(updatedAccount);
        Assert.Equal(1, updatedAccount!.SessionVersion);

        var validateResponse = await controller.Validate(
            new PublicYggdrasilController.YggdrasilAccessTokenRequest
            {
                AccessToken = token,
                ClientToken = Guid.NewGuid().ToString("N")
            },
            CancellationToken.None);
        var forbidden = Assert.IsType<ObjectResult>(validateResponse);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task HasJoined_WithoutJoin_ReturnsNoContent()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var options = BuildJwtOptions();
        var controller = new PublicYggdrasilController(
            fixture.DbContext,
            BuildConfiguration(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PublicYggdrasilController>.Instance);

        var response = await controller.HasJoined("MissingUser", "missing-server", CancellationToken.None);
        Assert.IsType<NoContentResult>(response);
    }

    [Fact]
    public async Task LegacyJoinAndCheckServer_ReturnsYes()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var options = BuildJwtOptions();

        var account = new AuthAccount
        {
            Username = "LegacyUser",
            ExternalId = "legacy-account",
            SessionVersion = 0,
            Roles = "player"
        };
        fixture.DbContext.AuthAccounts.Add(account);
        await fixture.DbContext.SaveChangesAsync();

        var token = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(options))
            .CreatePlayerToken(account, ["player"]);

        var controller = new PublicYggdrasilController(
            fixture.DbContext,
            BuildConfiguration(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PublicYggdrasilController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.ControllerContext.HttpContext.Request.QueryString =
            new QueryString($"?sessionId={Uri.EscapeDataString($"token:{token}:ffffffffffffffffffffffffffffffff")}&serverId=legacy-server");

        var legacyJoin = await controller.LegacyJoinServer(CancellationToken.None);
        var joinPayload = Assert.IsType<ContentResult>(legacyJoin);
        Assert.Equal("OK", joinPayload.Content);

        var legacyCheck = await controller.LegacyCheckServer("LegacyUser", "legacy-server", CancellationToken.None);
        var checkPayload = Assert.IsType<ContentResult>(legacyCheck);
        Assert.Equal("YES", checkPayload.Content);
    }

    [Fact]
    public async Task Metadata_ReturnsYggdrasilDescriptor()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var controller = new PublicYggdrasilController(
            fixture.DbContext,
            BuildConfiguration(),
            Microsoft.Extensions.Options.Options.Create(BuildJwtOptions()),
            NullLogger<PublicYggdrasilController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.Request.Scheme = "http";
        controller.ControllerContext.HttpContext.Request.Host = new HostString("95.217.99.17", 8080);

        var response = controller.Metadata();
        var ok = Assert.IsType<OkObjectResult>(response);
        var payload = JsonSerializer.SerializeToElement(ok.Value);

        Assert.True(payload.TryGetProperty("meta", out var metaNode));
        Assert.Equal("BivLauncher Auth", metaNode.GetProperty("serverName").GetString());
        Assert.True(payload.TryGetProperty("skinDomains", out var skinDomainsNode));
        Assert.Equal(JsonValueKind.Array, skinDomainsNode.ValueKind);
        Assert.False(payload.TryGetProperty("signaturePublickey", out _));
    }

    private static JwtOptions BuildJwtOptions()
    {
        return new JwtOptions
        {
            Secret = "test-secret-test-secret-test-secret-12345",
            Issuer = "bivlauncher",
            Audience = "bivlauncher-clients",
            ExpireMinutes = 60,
            PlayerExpireDays = 365
        };
    }

    private static IConfiguration BuildConfiguration(string launcherProof = "")
    {
        var values = new Dictionary<string, string?>
        {
            ["PUBLIC_BASE_URL"] = "http://95.217.99.17:8080",
            ["YGGDRASIL_SERVER_NAME"] = "BivLauncher Auth"
        };

        if (!string.IsNullOrWhiteSpace(launcherProof))
        {
            values["LAUNCHER_CLIENT_PROOF"] = launcherProof;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
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
