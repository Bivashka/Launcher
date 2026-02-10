using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace BivLauncher.Api.Tests;

public sealed class BuildPipelineServiceTests
{
    [Fact]
    public async Task RebuildProfileAsync_WhenSourceFileDisappears_SkipsMissingFileAndCompletes()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var temp = new TempDirectory();

        var profileSlug = "spicetech";
        var sourceRoot = Path.Combine(temp.Path, "BuildSources");
        var commonRoot = Path.Combine(sourceRoot, profileSlug, "common");
        Directory.CreateDirectory(commonRoot);

        var stableFilePath = Path.Combine(commonRoot, "a.txt");
        var disappearingFilePath = Path.Combine(commonRoot, "b.txt");
        await File.WriteAllTextAsync(stableFilePath, "stable-content");
        await File.WriteAllTextAsync(disappearingFilePath, "will-disappear");

        var profile = new Profile
        {
            Name = "SpiceTech",
            Slug = profileSlug
        };
        fixture.DbContext.Profiles.Add(profile);
        await fixture.DbContext.SaveChangesAsync();

        var storage = new RecordingObjectStorageService(uploadIndex =>
        {
            if (uploadIndex == 1 && File.Exists(disappearingFilePath))
            {
                File.Delete(disappearingFilePath);
            }
        });

        var buildService = new BuildPipelineService(
            fixture.DbContext,
            storage,
            Microsoft.Extensions.Options.Options.Create(new BuildPipelineOptions { SourceRoot = sourceRoot }),
            new TestWebHostEnvironment(temp.Path),
            NullLogger<BuildPipelineService>.Instance);

        var build = await buildService.RebuildProfileAsync(
            profile.Id,
            new ProfileRebuildRequest
            {
                LoaderType = "vanilla",
                McVersion = "1.5.2",
                PublishToServers = false
            },
            CancellationToken.None);

        Assert.Equal(BuildStatus.Completed, build.Status);
        Assert.Equal(1, build.FilesCount);

        var dbBuild = await fixture.DbContext.Builds.SingleAsync();
        Assert.Equal(BuildStatus.Completed, dbBuild.Status);
        Assert.Equal(1, dbBuild.FilesCount);

        Assert.Contains(storage.UploadedKeys, key => key.EndsWith("/a.txt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(storage.UploadedKeys, key => key.EndsWith("/b.txt", StringComparison.OrdinalIgnoreCase));

        var latestManifestKey = $"manifests/{profileSlug}/latest.json";
        var latestManifestBytes = storage.GetRequired(latestManifestKey);
        using var manifestDoc = JsonDocument.Parse(latestManifestBytes);
        var filesNode = manifestDoc.RootElement.GetProperty("files");
        Assert.Equal(1, filesNode.GetArrayLength());
        Assert.Equal("a.txt", filesNode[0].GetProperty("path").GetString());
    }

    private sealed class RecordingObjectStorageService(Action<int>? onUpload = null) : IObjectStorageService
    {
        private readonly Dictionary<string, StoredObject> _objects = new(StringComparer.OrdinalIgnoreCase);
        private int _uploadCount;

        public IReadOnlyCollection<string> UploadedKeys => _objects.Keys;

        public async Task UploadAsync(
            string key,
            Stream content,
            string contentType,
            IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            var uploadIndex = Interlocked.Increment(ref _uploadCount);
            onUpload?.Invoke(uploadIndex);

            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            _objects[key] = new StoredObject(buffer.ToArray(), contentType);
        }

        public Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            _objects.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task<StoredObjectMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
        {
            _objects.TryGetValue(key, out var value);
            if (value is null)
            {
                return Task.FromResult<StoredObjectMetadata?>(null);
            }

            var sha = ComputeSha256(value.Data);
            return Task.FromResult<StoredObjectMetadata?>(new StoredObjectMetadata(value.Data.Length, value.ContentType, sha));
        }

        public Task<IReadOnlyList<StoredObjectListItem>> ListByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            var normalizedPrefix = prefix.Trim();
            var items = _objects
                .Where(x => x.Key.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(x => new StoredObjectListItem(x.Key, x.Value.Data.LongLength, DateTime.UtcNow))
                .ToList();
            return Task.FromResult<IReadOnlyList<StoredObjectListItem>>(items);
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _objects.Remove(key);
            return Task.CompletedTask;
        }

        public byte[] GetRequired(string key)
        {
            if (!_objects.TryGetValue(key, out var value))
            {
                throw new InvalidOperationException($"Object '{key}' not found.");
            }

            return value.Data;
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(data);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "BivLauncher.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bivlauncher-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup for test temp files.
            }
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
