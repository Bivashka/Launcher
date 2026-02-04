using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection(BrandingOptions.SectionName));
builder.Services.Configure<S3Options>(builder.Configuration.GetSection(S3Options.SectionName));
builder.Services.Configure<NewsSyncOptions>(builder.Configuration.GetSection(NewsSyncOptions.SectionName));
builder.Services.Configure<NewsRetentionOptions>(builder.Configuration.GetSection(NewsRetentionOptions.SectionName));
builder.Services.Configure<RuntimeRetentionOptions>(builder.Configuration.GetSection(RuntimeRetentionOptions.SectionName));
builder.Services.Configure<BuildPipelineOptions>(builder.Configuration.GetSection(BuildPipelineOptions.SectionName));
builder.Services.Configure<AuthProviderOptions>(builder.Configuration.GetSection(AuthProviderOptions.SectionName));

var connectionString = builder.Configuration["DB_CONN"]
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=bivlauncher;Username=postgres;Password=postgres";

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<PasswordHasher<AdminUser>>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAdminAuditService, AdminAuditService>();
builder.Services.AddScoped<IBuildPipelineService, BuildPipelineService>();
builder.Services.AddScoped<IExternalAuthService, ExternalAuthService>();
builder.Services.AddScoped<INewsRetentionService, NewsRetentionService>();
builder.Services.AddScoped<IRuntimeRetentionService, RuntimeRetentionService>();
builder.Services.AddScoped<INewsImportService, NewsImportService>();
builder.Services.AddSingleton<IHardwareFingerprintService, HardwareFingerprintService>();
builder.Services.AddSingleton<IBrandingProvider, BrandingProvider>();
builder.Services.AddSingleton<IObjectStorageService, S3ObjectStorageService>();
builder.Services.AddSingleton<IAssetUrlService, AssetUrlService>();
builder.Services.AddHostedService<NewsSyncHostedService>();
builder.Services.AddHostedService<RuntimeRetentionHostedService>();

var adminOrigins = builder.Configuration["ADMIN_ALLOWED_ORIGINS"]?
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminClient", policy =>
    {
        policy.WithOrigins(adminOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var envJwtSecret = builder.Configuration["JWT_SECRET"];
if (!string.IsNullOrWhiteSpace(envJwtSecret))
{
    jwtOptions.Secret = envJwtSecret;
}

if (string.IsNullOrWhiteSpace(jwtOptions.Secret))
{
    jwtOptions.Secret = string.Empty;
}

if (string.IsNullOrWhiteSpace(jwtOptions.Secret))
{
    throw new InvalidOperationException("JWT secret is missing. Set JWT_SECRET or Jwt:Secret.");
}

builder.Services.PostConfigure<JwtOptions>(options =>
{
    options.Secret = jwtOptions.Secret;
    options.Issuer = string.IsNullOrWhiteSpace(options.Issuer) ? jwtOptions.Issuer : options.Issuer;
    options.Audience = string.IsNullOrWhiteSpace(options.Audience) ? jwtOptions.Audience : options.Audience;
    options.ExpireMinutes = options.ExpireMinutes <= 0 ? jwtOptions.ExpireMinutes : options.ExpireMinutes;
});

builder.Services.PostConfigure<S3Options>(options =>
{
    options.Endpoint = builder.Configuration["S3_ENDPOINT"] ?? options.Endpoint;
    options.Bucket = builder.Configuration["S3_BUCKET"] ?? options.Bucket;
    options.AccessKey = builder.Configuration["S3_ACCESS_KEY"] ?? options.AccessKey;
    options.SecretKey = builder.Configuration["S3_SECRET_KEY"] ?? options.SecretKey;

    var useSsl = builder.Configuration["S3_USE_SSL"];
    if (bool.TryParse(useSsl, out var parsedUseSsl))
    {
        options.UseSsl = parsedUseSsl;
    }

    var forcePathStyle = builder.Configuration["S3_FORCE_PATH_STYLE"];
    if (bool.TryParse(forcePathStyle, out var parsedForcePathStyle))
    {
        options.ForcePathStyle = parsedForcePathStyle;
    }

    var autoCreateBucket = builder.Configuration["S3_AUTO_CREATE_BUCKET"];
    if (bool.TryParse(autoCreateBucket, out var parsedAutoCreateBucket))
    {
        options.AutoCreateBucket = parsedAutoCreateBucket;
    }
});

builder.Services.PostConfigure<BuildPipelineOptions>(options =>
{
    options.SourceRoot = builder.Configuration["BUILD_SOURCE_ROOT"] ?? options.SourceRoot;
    options.DefaultJvmArgs = builder.Configuration["BUILD_DEFAULT_JVM_ARGS"] ?? options.DefaultJvmArgs;
    options.DefaultGameArgs = builder.Configuration["BUILD_DEFAULT_GAME_ARGS"] ?? options.DefaultGameArgs;
});

builder.Services.PostConfigure<NewsSyncOptions>(options =>
{
    var enabledRaw = builder.Configuration["NEWS_SYNC_ENABLED"];
    if (bool.TryParse(enabledRaw, out var enabled))
    {
        options.Enabled = enabled;
    }

    var intervalRaw = builder.Configuration["NEWS_SYNC_INTERVAL_MINUTES"];
    if (int.TryParse(intervalRaw, out var interval))
    {
        options.IntervalMinutes = Math.Clamp(interval, 5, 1440);
    }
});

builder.Services.PostConfigure<NewsRetentionOptions>(options =>
{
    var enabledRaw = builder.Configuration["NEWS_RETENTION_ENABLED"];
    if (bool.TryParse(enabledRaw, out var enabled))
    {
        options.Enabled = enabled;
    }

    var maxItemsRaw = builder.Configuration["NEWS_RETENTION_MAX_ITEMS"];
    if (int.TryParse(maxItemsRaw, out var maxItems))
    {
        options.MaxItems = Math.Clamp(maxItems, 50, 10000);
    }

    var maxAgeRaw = builder.Configuration["NEWS_RETENTION_MAX_AGE_DAYS"];
    if (int.TryParse(maxAgeRaw, out var maxAgeDays))
    {
        options.MaxAgeDays = Math.Clamp(maxAgeDays, 1, 3650);
    }
});

builder.Services.PostConfigure<RuntimeRetentionOptions>(options =>
{
    var enabledRaw = builder.Configuration["RUNTIME_RETENTION_ENABLED"];
    if (bool.TryParse(enabledRaw, out var enabled))
    {
        options.Enabled = enabled;
    }

    var intervalRaw = builder.Configuration["RUNTIME_RETENTION_INTERVAL_MINUTES"];
    if (int.TryParse(intervalRaw, out var interval))
    {
        options.IntervalMinutes = Math.Clamp(interval, 5, 10080);
    }

    var keepLastRaw = builder.Configuration["RUNTIME_RETENTION_KEEP_LAST"];
    if (int.TryParse(keepLastRaw, out var keepLast))
    {
        options.KeepLast = Math.Clamp(keepLast, 0, 100);
    }
});

builder.Services.PostConfigure<AuthProviderOptions>(options =>
{
    var explicitLoginUrl = builder.Configuration["AUTH_PROVIDER_LOGIN_URL"];
    if (!string.IsNullOrWhiteSpace(explicitLoginUrl))
    {
        options.LoginUrl = explicitLoginUrl.Trim();
    }

    var combined = builder.Configuration["AUTH_PROVIDER_URLS"];
    if (!string.IsNullOrWhiteSpace(combined))
    {
        var candidates = combined.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var candidate in candidates)
        {
            if (candidate.StartsWith("login=", StringComparison.OrdinalIgnoreCase))
            {
                options.LoginUrl = candidate["login=".Length..].Trim();
                break;
            }

            if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                options.LoginUrl = candidate.Trim();
                break;
            }
        }
    }

    var timeoutRaw = builder.Configuration["AUTH_PROVIDER_TIMEOUT_SECONDS"];
    if (int.TryParse(timeoutRaw, out var timeoutSeconds))
    {
        options.TimeoutSeconds = Math.Clamp(timeoutSeconds, 5, 120);
    }

    var allowDevFallbackRaw = builder.Configuration["AUTH_PROVIDER_ALLOW_DEV_FALLBACK"];
    if (bool.TryParse(allowDevFallbackRaw, out var allowDevFallback))
    {
        options.AllowDevFallback = allowDevFallback;
    }
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AdminClient");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();
