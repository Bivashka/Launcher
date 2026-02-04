using BivLauncher.Api.Data.Entities;

namespace BivLauncher.Api.Services;

public interface IJwtTokenService
{
    string CreateAdminToken(AdminUser adminUser);
    string CreatePlayerToken(AuthAccount authAccount, IReadOnlyList<string> roles);
}
