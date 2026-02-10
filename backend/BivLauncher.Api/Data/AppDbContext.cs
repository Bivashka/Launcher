using BivLauncher.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BivLauncher.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<AuthAccount> AuthAccounts => Set<AuthAccount>();
    public DbSet<TwoFactorConfig> TwoFactorConfigs => Set<TwoFactorConfig>();
    public DbSet<AuthProviderConfig> AuthProviderConfigs => Set<AuthProviderConfig>();
    public DbSet<NewsSourceConfig> NewsSourceConfigs => Set<NewsSourceConfig>();
    public DbSet<NewsSyncConfig> NewsSyncConfigs => Set<NewsSyncConfig>();
    public DbSet<NewsRetentionConfig> NewsRetentionConfigs => Set<NewsRetentionConfig>();
    public DbSet<RuntimeRetentionConfig> RuntimeRetentionConfigs => Set<RuntimeRetentionConfig>();
    public DbSet<S3StorageConfig> S3StorageConfigs => Set<S3StorageConfig>();
    public DbSet<InstallTelemetryConfig> InstallTelemetryConfigs => Set<InstallTelemetryConfig>();
    public DbSet<ProjectInstallStat> ProjectInstallStats => Set<ProjectInstallStat>();
    public DbSet<WizardPreflightRun> WizardPreflightRuns => Set<WizardPreflightRun>();
    public DbSet<DiscordRpcGlobalConfig> DiscordRpcGlobalConfigs => Set<DiscordRpcGlobalConfig>();
    public DbSet<SkinAsset> SkinAssets => Set<SkinAsset>();
    public DbSet<CapeAsset> CapeAssets => Set<CapeAsset>();
    public DbSet<DiscordRpcConfig> DiscordRpcConfigs => Set<DiscordRpcConfig>();
    public DbSet<HardwareBan> HardwareBans => Set<HardwareBan>();
    public DbSet<CrashReport> CrashReports => Set<CrashReport>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<NewsItem> NewsItems => Set<NewsItem>();
    public DbSet<DocumentationArticle> DocumentationArticles => Set<DocumentationArticle>();
    public DbSet<Build> Builds => Set<Build>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.Username).HasMaxLength(64);
            entity.Property(x => x.PasswordHash).HasMaxLength(512);
        });

        modelBuilder.Entity<AdminAuditLog>(entity =>
        {
            entity.Property(x => x.Action).HasMaxLength(128);
            entity.Property(x => x.Actor).HasMaxLength(64);
            entity.Property(x => x.EntityType).HasMaxLength(64);
            entity.Property(x => x.EntityId).HasMaxLength(256);
            entity.Property(x => x.RequestId).HasMaxLength(128);
            entity.Property(x => x.RemoteIp).HasMaxLength(64);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.Property(x => x.DetailsJson).HasMaxLength(8192);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => x.RequestId);
            entity.HasIndex(x => new { x.Action, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.Actor, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.RemoteIp, x.CreatedAtUtc });
        });

        modelBuilder.Entity<AuthAccount>(entity =>
        {
            entity.HasIndex(x => x.ExternalId).IsUnique();
            entity.HasIndex(x => x.Username);
            entity.Property(x => x.Username).HasMaxLength(64);
            entity.Property(x => x.ExternalId).HasMaxLength(128);
            entity.Property(x => x.Roles).HasMaxLength(512);
            entity.Property(x => x.SessionVersion).HasDefaultValue(0);
            entity.Property(x => x.HwidHash).HasMaxLength(128);
            entity.Property(x => x.DeviceUserName).HasMaxLength(128);
            entity.Property(x => x.TwoFactorSecret).HasMaxLength(128);
        });

        modelBuilder.Entity<TwoFactorConfig>(entity =>
        {
            entity.HasIndex(x => x.UpdatedAtUtc);
        });

        modelBuilder.Entity<AuthProviderConfig>(entity =>
        {
            entity.Property(x => x.AuthMode).HasMaxLength(16);
            entity.Property(x => x.LoginUrl).HasMaxLength(512);
            entity.Property(x => x.LoginFieldKey).HasMaxLength(64);
            entity.Property(x => x.PasswordFieldKey).HasMaxLength(64);
        });

        modelBuilder.Entity<S3StorageConfig>(entity =>
        {
            entity.Property(x => x.LocalRootPath).HasMaxLength(1024);
            entity.Property(x => x.Endpoint).HasMaxLength(512);
            entity.Property(x => x.Bucket).HasMaxLength(128);
            entity.Property(x => x.AccessKey).HasMaxLength(256);
            entity.Property(x => x.SecretKey).HasMaxLength(256);
        });

        modelBuilder.Entity<NewsSourceConfig>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Type).HasMaxLength(16);
            entity.Property(x => x.Url).HasMaxLength(1024);
            entity.Property(x => x.CacheEtag).HasMaxLength(512);
            entity.Property(x => x.CacheLastModified).HasMaxLength(128);
            entity.Property(x => x.LastSyncError).HasMaxLength(1024);
        });

        modelBuilder.Entity<NewsSyncConfig>(entity =>
        {
            entity.Property(x => x.LastRunError).HasMaxLength(1024);
        });

        modelBuilder.Entity<NewsRetentionConfig>(entity =>
        {
            entity.Property(x => x.LastError).HasMaxLength(1024);
        });

        modelBuilder.Entity<RuntimeRetentionConfig>(entity =>
        {
            entity.Property(x => x.LastRunError).HasMaxLength(1024);
        });

        modelBuilder.Entity<InstallTelemetryConfig>(entity =>
        {
            entity.HasIndex(x => x.UpdatedAtUtc);
        });

        modelBuilder.Entity<ProjectInstallStat>(entity =>
        {
            entity.HasIndex(x => x.ProjectKey).IsUnique();
            entity.HasIndex(x => x.LastSeenAtUtc);
            entity.Property(x => x.ProjectKey).HasMaxLength(128);
            entity.Property(x => x.ProjectName).HasMaxLength(128);
            entity.Property(x => x.LastLauncherVersion).HasMaxLength(64);
        });

        modelBuilder.Entity<WizardPreflightRun>(entity =>
        {
            entity.HasIndex(x => x.RanAtUtc);
            entity.HasIndex(x => new { x.Actor, x.RanAtUtc });
            entity.Property(x => x.Actor).HasMaxLength(64);
            entity.Property(x => x.ChecksJson).HasMaxLength(16384);
        });

        modelBuilder.Entity<SkinAsset>(entity =>
        {
            entity.HasIndex(x => x.AccountId).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(512);

            entity.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CapeAsset>(entity =>
        {
            entity.HasIndex(x => x.AccountId).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(512);

            entity.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DiscordRpcConfig>(entity =>
        {
            entity.HasIndex(x => new { x.ScopeType, x.ScopeId }).IsUnique();
            entity.Property(x => x.ScopeType).HasMaxLength(16);
            entity.Property(x => x.AppId).HasMaxLength(128);
            entity.Property(x => x.DetailsText).HasMaxLength(256);
            entity.Property(x => x.StateText).HasMaxLength(256);
            entity.Property(x => x.LargeImageKey).HasMaxLength(128);
            entity.Property(x => x.LargeImageText).HasMaxLength(128);
            entity.Property(x => x.SmallImageKey).HasMaxLength(128);
            entity.Property(x => x.SmallImageText).HasMaxLength(128);
        });

        modelBuilder.Entity<DiscordRpcGlobalConfig>(entity =>
        {
            entity.HasIndex(x => x.UpdatedAtUtc);
        });

        modelBuilder.Entity<HardwareBan>(entity =>
        {
            entity.HasIndex(x => x.AccountId);
            entity.HasIndex(x => x.HwidHash);
            entity.HasIndex(x => x.DeviceUserName);
            entity.Property(x => x.HwidHash).HasMaxLength(128);
            entity.Property(x => x.DeviceUserName).HasMaxLength(128);
            entity.Property(x => x.Reason).HasMaxLength(1024);

            entity.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CrashReport>(entity =>
        {
            entity.HasIndex(x => x.CrashId).IsUnique();
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            entity.HasIndex(x => x.ProfileSlug);
            entity.Property(x => x.CrashId).HasMaxLength(64);
            entity.Property(x => x.Status).HasMaxLength(16);
            entity.Property(x => x.ProfileSlug).HasMaxLength(64);
            entity.Property(x => x.ServerName).HasMaxLength(128);
            entity.Property(x => x.RouteCode).HasMaxLength(16);
            entity.Property(x => x.LauncherVersion).HasMaxLength(64);
            entity.Property(x => x.OsVersion).HasMaxLength(128);
            entity.Property(x => x.JavaVersion).HasMaxLength(128);
            entity.Property(x => x.Reason).HasMaxLength(512);
            entity.Property(x => x.ErrorType).HasMaxLength(128);
            entity.Property(x => x.LogExcerpt).HasMaxLength(16384);
            entity.Property(x => x.MetadataJson).HasMaxLength(4096);
        });

        modelBuilder.Entity<Profile>(entity =>
        {
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Slug).HasMaxLength(64);
            entity.Property(x => x.Description).HasMaxLength(2048);
            entity.Property(x => x.IconKey).HasMaxLength(512);
            entity.Property(x => x.JvmArgsDefault).HasMaxLength(4096);
            entity.Property(x => x.GameArgsDefault).HasMaxLength(4096);
            entity.Property(x => x.LatestBuildId).HasMaxLength(64);
            entity.Property(x => x.LatestManifestKey).HasMaxLength(512);
            entity.Property(x => x.LatestClientVersion).HasMaxLength(64);
            entity.Property(x => x.BundledJavaPath).HasMaxLength(512);
            entity.Property(x => x.BundledRuntimeKey).HasMaxLength(512);
            entity.Property(x => x.BundledRuntimeSha256).HasMaxLength(128);
            entity.Property(x => x.BundledRuntimeContentType).HasMaxLength(256);
        });

        modelBuilder.Entity<Server>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Address).HasMaxLength(255);
            entity.Property(x => x.MainJarPath).HasMaxLength(512);
            entity.Property(x => x.RuProxyAddress).HasMaxLength(255);
            entity.Property(x => x.RuJarPath).HasMaxLength(512);
            entity.Property(x => x.IconKey).HasMaxLength(512);
            entity.Property(x => x.LoaderType).HasMaxLength(32);
            entity.Property(x => x.McVersion).HasMaxLength(32);
            entity.Property(x => x.BuildId).HasMaxLength(64);

            entity.HasOne(x => x.Profile)
                .WithMany(x => x.Servers)
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Build>(entity =>
        {
            entity.Property(x => x.LoaderType).HasMaxLength(32);
            entity.Property(x => x.McVersion).HasMaxLength(32);
            entity.Property(x => x.Status).HasMaxLength(24);
            entity.Property(x => x.ManifestKey).HasMaxLength(512);
            entity.Property(x => x.ClientVersion).HasMaxLength(64);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2048);

            entity.HasOne(x => x.Profile)
                .WithMany(x => x.Builds)
                .HasForeignKey(x => x.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NewsItem>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(256);
            entity.Property(x => x.Body).HasMaxLength(8192);
            entity.Property(x => x.Source).HasMaxLength(256);
        });

        modelBuilder.Entity<DocumentationArticle>(entity =>
        {
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => new { x.Category, x.Order });
            entity.HasIndex(x => new { x.Published, x.UpdatedAtUtc });
            entity.Property(x => x.Slug).HasMaxLength(96);
            entity.Property(x => x.Title).HasMaxLength(256);
            entity.Property(x => x.Category).HasMaxLength(32);
            entity.Property(x => x.Summary).HasMaxLength(1024);
            entity.Property(x => x.BodyMarkdown).HasMaxLength(64000);
        });
    }
}
