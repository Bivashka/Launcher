using System.Security.Cryptography;
using System.Text;

namespace BivLauncher.Api.Services;

public sealed class TwoFactorService : ITwoFactorService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public string GenerateSecret()
    {
        Span<byte> bytes = stackalloc byte[20];
        RandomNumberGenerator.Fill(bytes);
        return ToBase32(bytes);
    }

    public bool ValidateCode(string secretBase32, string code, DateTime utcNow, int allowedDriftWindows = 1)
    {
        var normalizedCode = NormalizeCode(code);
        if (normalizedCode.Length != 6)
        {
            return false;
        }

        if (!TryFromBase32(secretBase32, out var secretBytes))
        {
            return false;
        }

        var timestep = GetCurrentTimeStepNumber(utcNow);
        for (var delta = -Math.Abs(allowedDriftWindows); delta <= Math.Abs(allowedDriftWindows); delta++)
        {
            var candidate = GenerateTotp(secretBytes, timestep + delta);
            if (candidate == normalizedCode)
            {
                return true;
            }
        }

        return false;
    }

    public string BuildOtpAuthUri(string issuer, string accountLabel, string secretBase32)
    {
        var normalizedIssuer = string.IsNullOrWhiteSpace(issuer) ? "BivLauncher" : issuer.Trim();
        var normalizedLabel = string.IsNullOrWhiteSpace(accountLabel) ? "player" : accountLabel.Trim();
        return $"otpauth://totp/{Uri.EscapeDataString(normalizedIssuer)}:{Uri.EscapeDataString(normalizedLabel)}?secret={Uri.EscapeDataString(secretBase32)}&issuer={Uri.EscapeDataString(normalizedIssuer)}&digits=6&period=30";
    }

    private static string GenerateTotp(byte[] secret, long timestep)
    {
        Span<byte> timestepBytes = stackalloc byte[8];
        var value = timestep;
        for (var i = 7; i >= 0; i--)
        {
            timestepBytes[i] = (byte)(value & 0xFF);
            value >>= 8;
        }

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(timestepBytes.ToArray());
        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
                         | (hash[offset + 1] << 16)
                         | (hash[offset + 2] << 8)
                         | hash[offset + 3];
        var otp = binaryCode % 1_000_000;
        return otp.ToString("D6");
    }

    private static long GetCurrentTimeStepNumber(DateTime utcNow)
    {
        var unix = new DateTimeOffset(utcNow).ToUnixTimeSeconds();
        return unix / 30;
    }

    private static string NormalizeCode(string rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return string.Empty;
        }

        var digits = new string(rawCode.Where(char.IsDigit).ToArray());
        return digits.Trim();
    }

    private static string ToBase32(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var outputLength = (int)Math.Ceiling(data.Length / 5d) * 8;
        var output = new StringBuilder(outputLength);

        var bitBuffer = 0;
        var bitCount = 0;
        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;
            while (bitCount >= 5)
            {
                var index = (bitBuffer >> (bitCount - 5)) & 0x1F;
                bitCount -= 5;
                output.Append(Base32Alphabet[index]);
            }
        }

        if (bitCount > 0)
        {
            var index = (bitBuffer << (5 - bitCount)) & 0x1F;
            output.Append(Base32Alphabet[index]);
        }

        while (output.Length % 8 != 0)
        {
            output.Append('=');
        }

        return output.ToString().TrimEnd('=');
    }

    private static bool TryFromBase32(string input, out byte[] result)
    {
        result = [];
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var cleaned = input.Trim().TrimEnd('=').ToUpperInvariant();
        if (cleaned.Length == 0)
        {
            return false;
        }

        var output = new List<byte>(cleaned.Length * 5 / 8);
        var bitBuffer = 0;
        var bitCount = 0;

        foreach (var c in cleaned)
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0)
            {
                return false;
            }

            bitBuffer = (bitBuffer << 5) | index;
            bitCount += 5;

            if (bitCount >= 8)
            {
                bitCount -= 8;
                output.Add((byte)((bitBuffer >> bitCount) & 0xFF));
            }
        }

        result = output.ToArray();
        return result.Length > 0;
    }
}
