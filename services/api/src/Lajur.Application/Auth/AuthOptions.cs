namespace Lajur.Application.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Lajur:Auth";

    public string Issuer { get; set; } = "Lajur";
    public string Audience { get; set; } = "Lajur.Android";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenIdleDays { get; set; } = 30;
    public int RefreshTokenAbsoluteDays { get; set; } = 180;
}

public sealed class StorageOptions
{
    public const string SectionName = "Lajur:Storage";

    public string RootPath { get; set; } = ".local/lajur";
}
