using BivLauncher.Api.Data;
using BivLauncher.Api.Options;
using BivLauncher.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace BivLauncher.Api.Tests;

public sealed class ExternalAuthServiceTests
{
    [Fact]
    public async Task AuthenticateAsync_WhenResponseHasSuccessButNoIdentity_ReturnsFailure()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = fixture.CreateService(
            """{"Success":true,"Message":"Auth endpoint is reachable"}""");

        var result = await service.AuthenticateAsync("player", "secret", string.Empty, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Auth provider returned success without identity fields.", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenResponseIsNotJson_ReturnsFailure()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = fixture.CreateService(
            "<html><body>ok</body></html>",
            mediaType: "text/html");

        var result = await service.AuthenticateAsync("player", "secret", string.Empty, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Auth provider returned invalid JSON.", result.ErrorMessage);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenResponseContainsIdentityWithoutSuccessFlag_ReturnsSuccess()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = fixture.CreateService(
            """{"externalId":"user-42","username":"player","roles":["player"],"banned":false}""");

        var result = await service.AuthenticateAsync("player", "secret", string.Empty, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("user-42", result.ExternalId);
        Assert.Equal("player", result.Username);
        Assert.Equal(["player"], result.Roles);
        Assert.False(result.Banned);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenResponseUsesLegacyLauncherIdentityFields_ReturnsSuccess()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = fixture.CreateService(
            """{"Success":true,"Login":"player","UserUuid":"user-42"}""");

        var result = await service.AuthenticateAsync("player", "secret", string.Empty, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("user-42", result.ExternalId);
        Assert.Equal("player", result.Username);
        Assert.Equal(["player"], result.Roles);
        Assert.False(result.Banned);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenResponseContainsExplicitFailure_ReturnsFailureMessage()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var service = fixture.CreateService(
            """{"success":false,"error":"Invalid username or password."}""");

        var result = await service.AuthenticateAsync("player", "secret", string.Empty, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Invalid username or password.", result.ErrorMessage);
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

        public ExternalAuthService CreateService(
            string responseBody,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string mediaType = "application/json")
        {
            return new ExternalAuthService(
                DbContext,
                Microsoft.Extensions.Options.Options.Create(new AuthProviderOptions
                {
                    AuthMode = "external",
                    LoginUrl = "https://auth.example/login",
                    LoginFieldKey = "username",
                    PasswordFieldKey = "password",
                    TimeoutSeconds = 5,
                    AllowDevFallback = false
                }),
                new StubHttpClientFactory(new StubHttpMessageHandler(
                    _ => new HttpResponseMessage(statusCode)
                    {
                        Content = new StringContent(responseBody, Encoding.UTF8, mediaType)
                    })),
                NullLogger<ExternalAuthService>.Instance);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler = handler;

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
