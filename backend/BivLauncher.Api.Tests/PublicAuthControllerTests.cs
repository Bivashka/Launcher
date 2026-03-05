using BivLauncher.Api.Contracts.Public;
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
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace BivLauncher.Api.Tests;

public sealed class PublicAuthControllerTests
{
    [Fact]
    public async Task Login_WhenLauncherVersionBelowMinimum_ReturnsForbidden()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var controller = CreateController(fixture.DbContext, minClientVersion: "2.0.0");
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.9.9";

        var response = await controller.Login(
            new PublicAuthLoginRequest
            {
                Username = "player",
                Password = "secret"
            },
            CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Login_WhenLauncherVersionMeetsMinimum_IssuesTokenWithLauncherVersionClaim()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var controller = CreateController(fixture.DbContext, minClientVersion: "2.0.0");
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/2.0.1";

        var response = await controller.Login(
            new PublicAuthLoginRequest
            {
                Username = "player",
                Password = "secret"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<PublicAuthLoginResponse>(ok.Value);
        Assert.False(string.IsNullOrWhiteSpace(payload.Token));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(payload.Token);
        var launcherVersion = jwt.Claims.FirstOrDefault(x => x.Type == "launcher_version")?.Value;
        Assert.Equal("2.0.1", launcherVersion);
    }

    [Fact]
    public async Task Session_WhenMinimumVersionConfiguredAndTokenHasNoLauncherVersionClaim_ReturnsUnauthorized()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var jwtService = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(BuildJwtOptions()));
        var account = new AuthAccount
        {
            Username = "player",
            ExternalId = "player-id",
            Roles = "player",
            SessionVersion = 0
        };
        fixture.DbContext.AuthAccounts.Add(account);
        await fixture.DbContext.SaveChangesAsync();

        var token = jwtService.CreatePlayerToken(account, ["player"]);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "Bearer"));

        var controller = CreateController(fixture.DbContext, minClientVersion: "2.0.0");
        controller.ControllerContext.HttpContext.User = principal;
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/2.0.1";

        var response = await controller.Session(CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Session_WhenProofConfiguredAndTokenHasNoProofIdClaim_ReturnsUnauthorized()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var jwtService = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(BuildJwtOptions()));
        var account = new AuthAccount
        {
            Username = "player",
            ExternalId = "player-proof-id",
            Roles = "player",
            SessionVersion = 0
        };
        fixture.DbContext.AuthAccounts.Add(account);
        await fixture.DbContext.SaveChangesAsync();

        var token = jwtService.CreatePlayerToken(account, ["player"], launcherVersion: "1.0.3");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "Bearer"));

        const string proof = "test-proof-secret";
        var controller = CreateController(fixture.DbContext, minClientVersion: string.Empty, launcherProof: proof);
        controller.ControllerContext.HttpContext.User = principal;
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.0.3";
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Proof"] = proof;

        var response = await controller.Session(CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Login_WhenProofConfigured_IssuesTokenWithProofIdClaim()
    {
        await using var fixture = await TestFixture.CreateAsync();
        const string proof = "test-proof-secret";

        var controller = CreateController(fixture.DbContext, minClientVersion: "1.0.0", launcherProof: proof);
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.0.3";
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Proof"] = proof;

        var response = await controller.Login(
            new PublicAuthLoginRequest
            {
                Username = "player",
                Password = "secret"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<PublicAuthLoginResponse>(ok.Value);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(payload.Token);
        var proofId = jwt.Claims.FirstOrDefault(x => x.Type == "launcher_proof_id")?.Value;
        Assert.False(string.IsNullOrWhiteSpace(proofId));
    }

    [Fact]
    public async Task Logout_WhenTokenIsValid_RevokesSessionVersion()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var controller = CreateController(fixture.DbContext, minClientVersion: "1.0.0");
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.2.3";

        var loginResult = await controller.Login(
            new PublicAuthLoginRequest
            {
                Username = "logout-player",
                Password = "secret"
            },
            CancellationToken.None);

        var loginOk = Assert.IsType<OkObjectResult>(loginResult.Result);
        var loginPayload = Assert.IsType<PublicAuthLoginResponse>(loginOk.Value);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(loginPayload.Token);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "Bearer"));

        var logoutController = CreateController(fixture.DbContext, minClientVersion: "1.0.0");
        logoutController.ControllerContext.HttpContext.User = principal;
        logoutController.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.2.3";

        var logoutResult = await logoutController.Logout(CancellationToken.None);
        Assert.IsType<NoContentResult>(logoutResult);

        var account = await fixture.DbContext.AuthAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == "logout-player");
        Assert.NotNull(account);
        Assert.Equal(1, account!.SessionVersion);
    }

    private static PublicAuthController CreateController(
        AppDbContext dbContext,
        string minClientVersion,
        string launcherProof = "")
    {
        var values = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(minClientVersion))
        {
            values["LAUNCHER_MIN_CLIENT_VERSION"] = minClientVersion;
        }

        if (!string.IsNullOrWhiteSpace(launcherProof))
        {
            values["LAUNCHER_CLIENT_PROOF"] = launcherProof;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var controller = new PublicAuthController(
            dbContext,
            configuration,
            new StubExternalAuthService(),
            new StubHardwareFingerprintService(),
            new JwtTokenService(Microsoft.Extensions.Options.Options.Create(BuildJwtOptions())),
            new StubTwoFactorService(),
            NullLogger<PublicAuthController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return controller;
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

    private sealed class StubExternalAuthService : IExternalAuthService
    {
        public Task<ExternalAuthResult> AuthenticateAsync(
            string username,
            string password,
            string hwidHash,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ExternalAuthResult
            {
                Success = true,
                ExternalId = $"{username}-id",
                Username = username,
                Roles = ["player"],
                Banned = false
            });
        }
    }

    private sealed class StubHardwareFingerprintService : IHardwareFingerprintService
    {
        public bool TryComputeHwidHash(string fingerprint, out string hwidHash, out string error)
        {
            hwidHash = string.IsNullOrWhiteSpace(fingerprint) ? string.Empty : fingerprint.Trim().ToLowerInvariant();
            error = string.Empty;
            return true;
        }

        public string NormalizeLegacyHash(string? hwidHash)
        {
            return (hwidHash ?? string.Empty).Trim().ToLowerInvariant();
        }
    }

    private sealed class StubTwoFactorService : ITwoFactorService
    {
        public string GenerateSecret()
        {
            return "SECRET";
        }

        public bool ValidateCode(string secretBase32, string code, DateTime utcNow, int allowedDriftWindows = 1)
        {
            return false;
        }

        public string BuildOtpAuthUri(string issuer, string accountLabel, string secretBase32)
        {
            return string.Empty;
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
