using System.Net;

namespace BivLauncher.Client.Services;

public sealed class LauncherApiException : InvalidOperationException
{
    public LauncherApiException(
        string message,
        HttpStatusCode statusCode,
        TimeSpan? retryAfter = null,
        string errorCode = "")
        : base(message)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
        ErrorCode = errorCode;
    }

    public HttpStatusCode StatusCode { get; }

    public TimeSpan? RetryAfter { get; }

    public string ErrorCode { get; }
}
