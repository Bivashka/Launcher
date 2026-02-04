using BivLauncher.Api.Contracts.Public;

namespace BivLauncher.Api.Services;

public interface IBrandingProvider
{
    Task<BrandingConfig> GetBrandingAsync(CancellationToken cancellationToken = default);
    Task<BrandingConfig> SaveBrandingAsync(BrandingConfig branding, CancellationToken cancellationToken = default);
}
