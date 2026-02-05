namespace BivLauncher.Api.Contracts.Admin;

public sealed record TwoFactorEnrollResponse(
    TwoFactorAccountDto Account,
    string Secret,
    string OtpAuthUri);
