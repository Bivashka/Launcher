using Microsoft.AspNetCore.Http;

namespace BivLauncher.Api.Services;

public interface IPlayerCosmeticsService
{
    Task<PlayerCosmeticUploadResult> UploadAsync(
        string user,
        string cosmeticType,
        IFormFile? file,
        string actor,
        string source,
        CancellationToken cancellationToken);
}

public sealed record PlayerCosmeticUploadResult(
    string Account,
    string Key,
    string Url);
