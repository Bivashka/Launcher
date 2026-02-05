using BivLauncher.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Text;
using Xunit;

namespace BivLauncher.Api.Tests;

public sealed class RateLimitPoliciesTests
{
    [Fact]
    public void BuildPartitionKey_UsesRemoteIpAndPath()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");
        context.Request.Path = "/api/public/auth/login";

        var key = RateLimitPolicies.BuildPartitionKey(context);

        Assert.Equal("203.0.113.5:/api/public/auth/login", key);
    }

    [Fact]
    public void BuildPartitionKey_UsesFallbacksWhenValuesMissing()
    {
        var context = new DefaultHttpContext();

        var key = RateLimitPolicies.BuildPartitionKey(context);

        Assert.Equal("unknown:", key);
    }

    [Fact]
    public async Task WriteRateLimitedResponseAsync_WithoutRetryAfter_WritesBasePayload()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await RateLimitPolicies.WriteRateLimitedResponseAsync(context, retryAfter: null);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        Assert.False(context.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal("{\"error\":\"Rate limit exceeded. Retry later.\"}", body);
    }

    [Fact]
    public async Task WriteRateLimitedResponseAsync_WithRetryAfter_WritesHeaderAndSeconds()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await RateLimitPolicies.WriteRateLimitedResponseAsync(context, TimeSpan.FromMilliseconds(1200));
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        Assert.True(context.Response.Headers.TryGetValue("Retry-After", out var retryAfterValue));
        Assert.Equal("2", retryAfterValue.ToString());
        Assert.Equal("{\"error\":\"Rate limit exceeded. Retry later.\",\"retryAfterSeconds\":2}", body);
    }
}
