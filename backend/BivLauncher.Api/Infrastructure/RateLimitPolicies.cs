using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace BivLauncher.Api.Infrastructure;

public static class RateLimitPolicies
{
    public const string PublicLoginPolicy = "PublicLogin";
    public const string PublicIngestPolicy = "PublicIngest";
    public const string AdminAuthPolicy = "AdminAuth";

    private const string RateLimitError = "{\"error\":\"Rate limit exceeded. Retry later.\"}";

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            TimeSpan? retryAfter = null;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue) &&
                retryAfterValue > TimeSpan.Zero)
            {
                retryAfter = retryAfterValue;
            }

            await WriteRateLimitedResponseAsync(context.HttpContext, retryAfter, cancellationToken);
        };

        options.AddPolicy(PublicLoginPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: BuildPartitionKey(context),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 12,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                }));

        options.AddPolicy(PublicIngestPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: BuildPartitionKey(context),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 40,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                }));

        options.AddPolicy(AdminAuthPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: BuildPartitionKey(context),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 20,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                }));
    }

    public static string BuildPartitionKey(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = context.Request.Path.Value ?? "no-path";
        return $"{remoteIp}:{path}";
    }

    public static Task WriteRateLimitedResponseAsync(
        HttpContext context,
        TimeSpan? retryAfter,
        CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";

        if (retryAfter is null || retryAfter.Value <= TimeSpan.Zero)
        {
            return context.Response.WriteAsync(RateLimitError, cancellationToken);
        }

        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.Value.TotalSeconds));
        context.Response.Headers["Retry-After"] = seconds.ToString();
        return context.Response.WriteAsync(
            $"{{\"error\":\"Rate limit exceeded. Retry later.\",\"retryAfterSeconds\":{seconds}}}",
            cancellationToken);
    }
}
