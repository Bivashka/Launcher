using System.Net;

namespace BivLauncher.Client.Services;

public sealed class LauncherApiException : InvalidOperationException
{
    public LauncherApiException(string message, HttpStatusCode statusCode, TimeSpan? retryAfter = null)
        : base(message)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    public TimeSpan? RetryAfter { get; }
}
