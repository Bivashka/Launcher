namespace BivLauncher.Api.Services;

public interface IHardwareFingerprintService
{
    bool TryComputeHwidHash(string fingerprint, out string hwidHash, out string error);
    string NormalizeLegacyHash(string? hwidHash);
}
