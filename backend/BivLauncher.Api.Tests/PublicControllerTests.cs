using BivLauncher.Api.Contracts.Admin;
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
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace BivLauncher.Api.Tests;

public sealed class PublicControllerTests
{
    [Fact]
    public async Task Bootstrap_WhenProfileIsPrivateAndRequestIsAnonymous_HidesPrivateProfile()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedProfileAsync("public", isPrivate: false, allowedPlayerUsernames: string.Empty);
        await fixture.SeedProfileAsync("private", isPrivate: true, allowedPlayerUsernames: "tester");

        var controller = fixture.CreateController();

        var response = await controller.Bootstrap(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<BootstrapResponse>(ok.Value);
        Assert.Contains(payload.Profiles, profile => profile.Slug == "public");
        Assert.DoesNotContain(payload.Profiles, profile => profile.Slug == "private");
    }

    [Fact]
    public async Task Bootstrap_WhenProfileIsPrivateAndUserIsAllowlisted_ReturnsPrivateProfileServers()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedProfileAsync("private", isPrivate: true, allowedPlayerUsernames: "tester");

        var controller = fixture.CreateController(username: "tester");

        var response = await controller.Bootstrap(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<BootstrapResponse>(ok.Value);
        var profile = Assert.Single(payload.Profiles);
        Assert.Equal("private", profile.Slug);
        Assert.Single(profile.Servers);
        Assert.Equal("private-server", profile.Servers[0].Name);
    }

    [Fact]
    public async Task Bootstrap_WhenBrandingHasLauncherIconKey_ReturnsResolvedLauncherIconUrl()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedProfileAsync("public", isPrivate: false, allowedPlayerUsernames: string.Empty);

        var controller = fixture.CreateController(
            brandingProvider: new StubBrandingProvider(
                new BrandingConfig(
                    ProductName: "BivLauncher",
                    LauncherDirectoryName: "BivLauncher",
                    DeveloperName: "BivLauncher",
                    Tagline: string.Empty,
                    SupportUrl: string.Empty,
                    PrimaryColor: string.Empty,
                    AccentColor: string.Empty,
                    SurfaceColor: string.Empty,
                    SurfaceBorderColor: string.Empty,
                    TextPrimaryColor: string.Empty,
                    TextSecondaryColor: string.Empty,
                    PrimaryButtonColor: string.Empty,
                    PrimaryButtonBorderColor: string.Empty,
                    PrimaryButtonTextColor: string.Empty,
                    PlayButtonColor: string.Empty,
                    PlayButtonBorderColor: string.Empty,
                    PlayButtonTextColor: string.Empty,
                    InputBackgroundColor: string.Empty,
                    InputBorderColor: string.Empty,
                    InputTextColor: string.Empty,
                    ListBackgroundColor: string.Empty,
                    ListBorderColor: string.Empty,
                    LogoText: "BLP",
                    LauncherIconKey: "branding/icon/custom.ico",
                    LauncherIconUrl: string.Empty,
                    BackgroundImageUrl: string.Empty,
                    BackgroundOverlayOpacity: 0.55,
                    LoginCardPosition: "center",
                    LoginCardWidth: 460)));

        var response = await controller.Bootstrap(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<BootstrapResponse>(ok.Value);
        Assert.Equal("branding/icon/custom.ico", payload.Branding.LauncherIconKey);
        Assert.Equal("https://cdn.local/branding/icon/custom.ico", payload.Branding.LauncherIconUrl);
    }

    [Fact]
    public async Task Manifest_WhenProfileIsPrivateAndUserIsNotAllowlisted_ReturnsNotFound()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedManifestAsync("private", isPrivate: true, allowedPlayerUsernames: "tester");

        var controller = fixture.CreateController(username: "outsider");

        var response = await controller.Manifest("private", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(response);
    }

    [Fact]
    public async Task Manifest_WhenProfileIsPrivateAndUserIsAllowlisted_ReturnsManifest()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedManifestAsync("private", isPrivate: true, allowedPlayerUsernames: "tester");

        var controller = fixture.CreateController(username: "tester");

        var response = await controller.Manifest("private", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        var payload = Assert.IsType<LauncherManifest>(ok.Value);
        Assert.Equal("private", payload.ProfileSlug);
        Assert.Equal("build-private", payload.BuildId);
    }

    [Fact]
    public async Task Manifest_ResolvesRuntimeAndFileDownloadUrls_FromDeliverySettings()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedManifestAsync(
            "public",
            isPrivate: false,
            allowedPlayerUsernames: string.Empty,
            "runtime/public-runtime.zip",
            [
                new LauncherManifestFile(
                    Path: "mods/example.jar",
                    Sha256: "abc",
                    Size: 123,
                    S3Key: "clients/public/build-public/mods/example.jar")
            ]);

        var controller = fixture.CreateController();
        var response = await controller.Manifest("public", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        var payload = Assert.IsType<LauncherManifest>(ok.Value);
        Assert.Equal("https://cdn.local/runtime/public-runtime.zip", payload.JavaRuntimeArtifactUrl);
        var file = Assert.Single(payload.Files);
        Assert.Equal("https://cdn.local/clients/public/build-public/mods/example.jar", file.DownloadUrl);
    }

    [Fact]
    public async Task GetAsset_ReturnsStreamedFileResult()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.ObjectStorage.UploadAsync(
            "branding/background/example.png",
            new MemoryStream([1, 2, 3, 4]),
            "image/png");

        var controller = fixture.CreateController();
        var response = await controller.GetAsset("branding/background/example.png", CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(response);
        Assert.Equal("image/png", file.ContentType);
        Assert.True(file.EnableRangeProcessing);

        using var buffer = new MemoryStream();
        await file.FileStream.CopyToAsync(buffer);
        Assert.Equal([1, 2, 3, 4], buffer.ToArray());
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly InMemoryObjectStorageService _objectStorage = new();

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

        public async Task SeedProfileAsync(string slug, bool isPrivate, string allowedPlayerUsernames)
        {
            var profile = new Profile
            {
                Name = slug,
                Slug = slug,
                Description = $"{slug} profile",
                Enabled = true,
                IsPrivate = isPrivate,
                AllowedPlayerUsernames = allowedPlayerUsernames,
                Priority = 100,
                RecommendedRamMb = 2048
            };
            profile.Servers.Add(new Server
            {
                ProfileId = profile.Id,
                Name = $"{slug}-server",
                Address = "127.0.0.1",
                Port = 25565,
                Enabled = true,
                Order = 100
            });

            DbContext.Profiles.Add(profile);
            await DbContext.SaveChangesAsync();
        }

        public InMemoryObjectStorageService ObjectStorage => _objectStorage;

        public async Task SeedManifestAsync(
            string slug,
            bool isPrivate,
            string allowedPlayerUsernames,
            string? javaRuntimeArtifactKey = null,
            IReadOnlyList<LauncherManifestFile>? files = null)
        {
            var profile = new Profile
            {
                Name = slug,
                Slug = slug,
                Description = $"{slug} profile",
                Enabled = true,
                IsPrivate = isPrivate,
                AllowedPlayerUsernames = allowedPlayerUsernames,
                LatestManifestKey = $"manifests/{slug}.json",
                Priority = 100,
                RecommendedRamMb = 2048
            };

            DbContext.Profiles.Add(profile);
            await DbContext.SaveChangesAsync();

            var manifest = new LauncherManifest(
                ProfileSlug: slug,
                BuildId: $"build-{slug}",
                LoaderType: "vanilla",
                McVersion: "1.21.1",
                ClientVersion: "1.0.0",
                CreatedAtUtc: DateTime.UtcNow,
                JvmArgsDefault: "-Xmx2G",
                GameArgsDefault: string.Empty,
                JavaRuntime: null,
                JavaRuntimeArtifactKey: javaRuntimeArtifactKey,
                JavaRuntimeArtifactSha256: null,
                JavaRuntimeArtifactSizeBytes: null,
                JavaRuntimeArtifactContentType: null,
                JavaRuntimeArtifactUrl: null,
                Files: files ?? []);

            await _objectStorage.UploadJsonAsync(profile.LatestManifestKey, manifest);
        }

        public PublicController CreateController(string username = "", IBrandingProvider? brandingProvider = null)
        {
            var controller = new PublicController(
                DbContext,
                brandingProvider ?? new StubBrandingProvider(),
                new StubBuildPipelineService(),
                new StubLauncherUpdateConfigProvider(),
                new ConfigurationBuilder().AddInMemoryCollection().Build(),
                new StubDeliverySettingsProvider(new DeliverySettingsConfig(
                    PublicBaseUrl: "https://cdn.local",
                    AssetBaseUrl: "https://cdn.local",
                    FallbackApiBaseUrls: [],
                    UpdatedAtUtc: null)),
                new StubAssetUrlService(),
                _objectStorage,
                Microsoft.Extensions.Options.Options.Create(new InstallTelemetryOptions { Enabled = true }),
                Microsoft.Extensions.Options.Options.Create(new DiscordRpcOptions { Enabled = true, PrivacyMode = false }),
                NullLogger<PublicController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.Name, username)
                    ],
                    authenticationType: "Bearer"));
            }

            return controller;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class StubBrandingProvider(BrandingConfig? branding = null) : IBrandingProvider
    {
        public Task<BrandingConfig> GetBrandingAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(branding ?? new BrandingConfig(
                ProductName: "BivLauncher",
                LauncherDirectoryName: "BivLauncher",
                DeveloperName: "BivLauncher",
                Tagline: string.Empty,
                SupportUrl: string.Empty,
                PrimaryColor: string.Empty,
                AccentColor: string.Empty,
                SurfaceColor: string.Empty,
                SurfaceBorderColor: string.Empty,
                TextPrimaryColor: string.Empty,
                TextSecondaryColor: string.Empty,
                PrimaryButtonColor: string.Empty,
                PrimaryButtonBorderColor: string.Empty,
                PrimaryButtonTextColor: string.Empty,
                PlayButtonColor: string.Empty,
                PlayButtonBorderColor: string.Empty,
                PlayButtonTextColor: string.Empty,
                InputBackgroundColor: string.Empty,
                InputBorderColor: string.Empty,
                InputTextColor: string.Empty,
                ListBackgroundColor: string.Empty,
                ListBorderColor: string.Empty,
                LogoText: "BLP",
                LauncherIconKey: string.Empty,
                LauncherIconUrl: string.Empty,
                BackgroundImageUrl: string.Empty,
                BackgroundOverlayOpacity: 0.55,
                LoginCardPosition: "center",
                LoginCardWidth: 460));
        }

        public Task<BrandingConfig> SaveBrandingAsync(BrandingConfig branding, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(branding);
        }
    }

    private sealed class StubBuildPipelineService : IBuildPipelineService
    {
        public Task<BuildDto> RebuildProfileAsync(Guid profileId, ProfileRebuildRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Build pipeline should not be called in these tests.");
        }
    }

    private sealed class StubLauncherUpdateConfigProvider : ILauncherUpdateConfigProvider
    {
        public Task<LauncherUpdateConfig?> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<LauncherUpdateConfig?>(null);
        }

        public Task<LauncherUpdateConfig> SaveAsync(LauncherUpdateConfig config, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(config);
        }
    }

    private sealed class StubAssetUrlService : IAssetUrlService
    {
        public string BuildPublicUrl(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? string.Empty : $"https://cdn.local/{key}";
        }
    }

    private sealed class InMemoryObjectStorageService : IObjectStorageService
    {
        private readonly Dictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);

        public async Task UploadAsync(
            string key,
            Stream content,
            string contentType,
            IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            _objects[key] = new StoredObject(buffer.ToArray(), contentType);
        }

        public Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            _objects.TryGetValue(key, out var storedObject);
            return Task.FromResult(storedObject);
        }

        public Task<StoredObjectStream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            if (!_objects.TryGetValue(key, out var storedObject))
            {
                return Task.FromResult<StoredObjectStream?>(null);
            }

            return Task.FromResult<StoredObjectStream?>(
                new StoredObjectStream(
                    new MemoryStream(storedObject.Data, writable: false),
                    storedObject.ContentType,
                    storedObject.Data.LongLength));
        }

        public Task<StoredObjectMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StoredObjectMetadata?>(null);
        }

        public Task<IReadOnlyList<StoredObjectListItem>> ListByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StoredObjectListItem>>([]);
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _objects.Remove(key);
            return Task.CompletedTask;
        }

        public Task UploadJsonAsync<T>(string key, T payload, CancellationToken cancellationToken = default)
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(payload);
            _objects[key] = new StoredObject(data, "application/json");
            return Task.CompletedTask;
        }
    }
}
