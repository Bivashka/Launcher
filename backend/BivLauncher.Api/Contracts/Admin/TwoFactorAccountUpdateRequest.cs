namespace BivLauncher.Api.Contracts.Admin;

public sealed class TwoFactorAccountUpdateRequest
{
    public bool TwoFactorRequired { get; set; }
}
