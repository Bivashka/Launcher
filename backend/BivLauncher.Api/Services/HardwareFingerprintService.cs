using System.Security.Cryptography;
using System.Text;

namespace BivLauncher.Api.Services;

public sealed class HardwareFingerprintService(IConfiguration configuration) : IHardwareFingerprintService
{
    public bool TryComputeHwidHash(string fingerprint, out string hwidHash, out string error)
    {
        hwidHash = string.Empty;
        error = string.Empty;

        var normalizedFingerprint = NormalizeLegacyHash(fingerprint);
        if (string.IsNullOrWhiteSpace(normalizedFingerprint))
        {
            return true;
        }

        var salt = (configuration["HWID_HMAC_SALT"] ?? configuration["Hwid:HmacSalt"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(salt))
        {
            error = "HWID_HMAC_SALT is not configured.";
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(salt));
        var bytes = Encoding.UTF8.GetBytes(normalizedFingerprint);
        hwidHash = Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant();
        return true;
    }

    public string NormalizeLegacyHash(string? hwidHash)
    {
        if (string.IsNullOrWhiteSpace(hwidHash))
        {
            return string.Empty;
        }

        var normalized = hwidHash.Trim().ToLowerInvariant();
        return normalized.Length > 128 ? normalized[..128] : normalized;
    }
}
