using System.Security.Cryptography;
using System.Text;

namespace BivLauncher.Client.Services;

internal static class LocalSecretProtector
{
    private const string ProtectedPrefix = "enc:v1:dpapi:";
    private const string TokenScope = "token:";
    private const string EndpointScope = "endpoint:";
    private static readonly byte[] TokenProtectionEntropy = Encoding.UTF8.GetBytes("BivLauncher.TokenProtection.v1");
    private static readonly byte[] EndpointProtectionEntropy = Encoding.UTF8.GetBytes("BivLauncher.EndpointProtection.v1");

    public static string ProtectToken(string? rawToken)
    {
        return Protect(
            rawToken,
            TokenScope,
            TokenProtectionEntropy,
            preservePlaintextWhenProtectionUnavailable: false);
    }

    public static string UnprotectToken(string? persistedToken)
    {
        return Unprotect(persistedToken, TokenScope, TokenProtectionEntropy);
    }

    public static string ProtectEndpoint(string? rawEndpoint)
    {
        return Protect(
            rawEndpoint,
            EndpointScope,
            EndpointProtectionEntropy,
            preservePlaintextWhenProtectionUnavailable: true);
    }

    public static string UnprotectEndpoint(string? persistedEndpoint)
    {
        return Unprotect(persistedEndpoint, EndpointScope, EndpointProtectionEntropy);
    }

    public static bool IsProtectedToken(string? value)
    {
        return IsProtected(value, TokenScope);
    }

    public static bool IsProtectedEndpoint(string? value)
    {
        return IsProtected(value, EndpointScope);
    }

    private static string Protect(
        string? rawValue,
        string scope,
        byte[] entropy,
        bool preservePlaintextWhenProtectionUnavailable)
    {
        var value = (rawValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (IsProtected(value, scope))
        {
            return value;
        }

        if (!OperatingSystem.IsWindows())
        {
            return preservePlaintextWhenProtectionUnavailable
                ? value
                : string.Empty;
        }

        try
        {
            var valueBytes = Encoding.UTF8.GetBytes(value);
            var protectedBytes = ProtectedData.Protect(valueBytes, entropy, DataProtectionScope.CurrentUser);
            return ProtectedPrefix + scope + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return preservePlaintextWhenProtectionUnavailable
                ? value
                : string.Empty;
        }
    }

    private static string Unprotect(string? persistedValue, string scope, byte[] entropy)
    {
        var value = (persistedValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!IsProtected(value, scope))
        {
            return value;
        }

        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        var payload = value[(ProtectedPrefix.Length + scope.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(payload);
            var unprotectedBytes = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(unprotectedBytes).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsProtected(string? value, string scope)
    {
        return (value ?? string.Empty).Trim().StartsWith(ProtectedPrefix + scope, StringComparison.Ordinal);
    }
}
