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
    public async Task Login_WhenUsernameIsConfiguredAsLauncherAdmin_ReturnsAdminRole()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var controller = CreateController(
            fixture.DbContext,
            minClientVersion: "1.0.0",
            securitySettingsProvider: new StubSecuritySettingsProvider(new SecuritySettingsConfig(
                MaxConcurrentGameAccountsPerDevice: 1,
                LauncherAdminUsernames: ["player"],
                SiteCosmeticsUploadSecret: string.Empty,
                GameSessionHeartbeatIntervalSeconds: 45,
                GameSessionExpirationSeconds: 150,
                UpdatedAtUtc: null)));
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.0.3";

        var response = await controller.Login(
            new PublicAuthLoginRequest
            {
                Username = "player",
                Password = "secret"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<PublicAuthLoginResponse>(ok.Value);
        Assert.Contains("admin", payload.Roles, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportSecurityViolation_WhenPlayerSessionIsValid_CreatesTimedHardwareBan()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var loginController = CreateController(fixture.DbContext, minClientVersion: "1.0.0");
        loginController.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.2.3";

        var loginResult = await loginController.Login(
            new PublicAuthLoginRequest
            {
                Username = "tamper-player",
                Password = "secret",
                HwidFingerprint = "hwid-42",
                DeviceUserName = "pc-42"
            },
            CancellationToken.None);

        var loginOk = Assert.IsType<OkObjectResult>(loginResult.Result);
        var loginPayload = Assert.IsType<PublicAuthLoginResponse>(loginOk.Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(loginPayload.Token);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "Bearer"));

        var controller = CreateController(fixture.DbContext, minClientVersion: "1.0.0");
        controller.ControllerContext.HttpContext.User = principal;
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.2.3";

        var response = await controller.ReportSecurityViolation(
            new PublicSecurityViolationReportRequest
            {
                Reason = "Suspicious module detected",
                Evidence = "javaw:frida-agent.dll",
                HwidFingerprint = "hwid-42",
                DeviceUserName = "pc-42"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<PublicSecurityViolationReportResponse>(ok.Value);
        Assert.True(payload.Banned);
        Assert.False(payload.Exempt);
        Assert.NotNull(payload.ExpiresAtUtc);

        var account = await fixture.DbContext.AuthAccounts.SingleAsync(x => x.Username == "tamper-player");
        Assert.Equal(1, account.SessionVersion);

        var ban = await fixture.DbContext.HardwareBans.SingleAsync(x => x.AccountId == account.Id);
        Assert.Equal("hwid-42", ban.HwidHash);
        Assert.Equal("pc-42", ban.DeviceUserName);
        Assert.Equal(payload.ExpiresAtUtc, ban.ExpiresAtUtc);
    }

    [Fact]
    public async Task ReportSecurityViolation_WhenAdminSessionIsValid_ReturnsExemptWithoutBan()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var securitySettings = new StubSecuritySettingsProvider(new SecuritySettingsConfig(
            MaxConcurrentGameAccountsPerDevice: 1,
            LauncherAdminUsernames: ["admin-player"],
            SiteCosmeticsUploadSecret: string.Empty,
            GameSessionHeartbeatIntervalSeconds: 45,
            GameSessionExpirationSeconds: 150,
            UpdatedAtUtc: null));

        var loginController = CreateController(
            fixture.DbContext,
            minClientVersion: "1.0.0",
            securitySettingsProvider: securitySettings);
        loginController.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.2.3";

        var loginResult = await loginController.Login(
            new PublicAuthLoginRequest
            {
                Username = "admin-player",
                Password = "secret",
                HwidFingerprint = "admin-hwid",
                DeviceUserName = "admin-pc"
            },
            CancellationToken.None);

        var loginOk = Assert.IsType<OkObjectResult>(loginResult.Result);
        var loginPayload = Assert.IsType<PublicAuthLoginResponse>(loginOk.Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(loginPayload.Token);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "Bearer"));

        var controller = CreateController(
            fixture.DbContext,
            minClientVersion: "1.0.0",
            securitySettingsProvider: securitySettings);
        controller.ControllerContext.HttpContext.User = principal;
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.2.3";

        var response = await controller.ReportSecurityViolation(
            new PublicSecurityViolationReportRequest
            {
                Reason = "Debugger attached",
                Evidence = "Debugger.IsAttached",
                HwidFingerprint = "admin-hwid",
                DeviceUserName = "admin-pc"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<PublicSecurityViolationReportResponse>(ok.Value);
        Assert.False(payload.Banned);
        Assert.True(payload.Exempt);
        Assert.Null(payload.ExpiresAtUtc);
        Assert.Equal(0, await fixture.DbContext.HardwareBans.CountAsync());
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

    [Fact]
    public async Task Login_WhenUsernameIsLinkedToLegacyExternalId_RelinksAccountToProviderExternalId()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AuthAccounts.Add(new AuthAccount
        {
            Username = "Bivashka",
            ExternalId = "Bivashka",
            Roles = "player",
            SessionVersion = 0
        });
        await fixture.DbContext.SaveChangesAsync();

        var controller = CreateController(fixture.DbContext, minClientVersion: "1.0.0");
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.2.3";

        var response = await controller.Login(
            new PublicAuthLoginRequest
            {
                Username = "Bivashka",
                Password = "secret"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<PublicAuthLoginResponse>(ok.Value);
        Assert.Equal("Bivashka", payload.Username);
        Assert.Equal("Bivashka-id", payload.ExternalId);

        var account = await fixture.DbContext.AuthAccounts
            .AsNoTracking()
            .SingleAsync(x => x.Username == "Bivashka");
        Assert.Equal("Bivashka-id", account.ExternalId);
    }

    [Fact]
    public async Task Login_WhenAuthProviderRateLimitsKnownDevice_UsesLocalFallback()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AuthAccounts.Add(new AuthAccount
        {
            Username = "Freaking",
            ExternalId = "Freaking-id",
            Roles = "player",
            SessionVersion = 0,
            HwidHash = "hwid-1",
            DeviceUserName = "pc-1"
        });
        await fixture.DbContext.SaveChangesAsync();

        var controller = CreateController(
            fixture.DbContext,
            minClientVersion: "1.0.0",
            externalAuthService: new StubExternalAuthService
            {
                Failure = new ExternalAuthResult
                {
                    Success = false,
                    ErrorMessage = "Слишком много попыток входа. Попробуйте позже.",
                    StatusCode = System.Net.HttpStatusCode.TooManyRequests
                }
            });
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.2.3";

        var response = await controller.Login(
            new PublicAuthLoginRequest
            {
                Username = "Freaking",
                Password = "secret",
                HwidFingerprint = "hwid-1",
                DeviceUserName = "pc-1"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<PublicAuthLoginResponse>(ok.Value);
        Assert.Equal("Freaking", payload.Username);
        Assert.Equal("Freaking-id", payload.ExternalId);
    }

    [Fact]
    public async Task Login_WhenAuthProviderRateLimitsUnknownDevice_ReturnsServiceUnavailable()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AuthAccounts.Add(new AuthAccount
        {
            Username = "Freaking",
            ExternalId = "Freaking-id",
            Roles = "player",
            SessionVersion = 0,
            HwidHash = "hwid-1",
            DeviceUserName = "pc-1"
        });
        await fixture.DbContext.SaveChangesAsync();

        var controller = CreateController(
            fixture.DbContext,
            minClientVersion: "1.0.0",
            externalAuthService: new StubExternalAuthService
            {
                Failure = new ExternalAuthResult
                {
                    Success = false,
                    ErrorMessage = "Слишком много попыток входа. Попробуйте позже.",
                    StatusCode = System.Net.HttpStatusCode.TooManyRequests
                }
            });
        controller.ControllerContext.HttpContext.Request.Headers["X-BivLauncher-Client"] = "BivLauncher.Client/1.2.3";

        var response = await controller.Login(
            new PublicAuthLoginRequest
            {
                Username = "Freaking",
                Password = "secret",
                HwidFingerprint = "other-hwid",
                DeviceUserName = "other-pc"
            },
            CancellationToken.None);

        var unavailable = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
    }

    private static PublicAuthController CreateController(
        AppDbContext dbContext,
        string minClientVersion,
        string launcherProof = "",
        ISecuritySettingsProvider? securitySettingsProvider = null,
        IExternalAuthService? externalAuthService = null)
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
            externalAuthService ?? new StubExternalAuthService(),
            new StubHardwareFingerprintService(),
            new JwtTokenService(Microsoft.Extensions.Options.Options.Create(BuildJwtOptions())),
            new StubTwoFactorService(),
            securitySettingsProvider ?? new StubSecuritySettingsProvider(),
            new AdminAuditService(
                dbContext,
                new HttpContextAccessor(),
                NullLogger<AdminAuditService>.Instance),
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
        public ExternalAuthResult? Failure { get; init; }

        public Task<ExternalAuthResult> AuthenticateAsync(
            string username,
            string password,
            string hwidHash,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromResult(Failure);
            }

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
