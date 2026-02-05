namespace BivLauncher.Api.Services;

public interface ITwoFactorService
{
    string GenerateSecret();
    bool ValidateCode(string secretBase32, string code, DateTime utcNow, int allowedDriftWindows = 1);
    string BuildOtpAuthUri(string issuer, string accountLabel, string secretBase32);
}
