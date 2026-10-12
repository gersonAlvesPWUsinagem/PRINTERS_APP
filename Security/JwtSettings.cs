namespace PRINTERS_APP.Security;

public sealed class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int UserTokenSeconds { get; init; } = 50;
    public int DeviceTokenDays { get; init; } = 360;
    public int ClockSkewSeconds { get; init; }
}
