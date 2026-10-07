namespace Lajur.Api.Web;

/// <summary>
/// The owner's local date for report formulas that use Excel's TODAY() ("hari berjalan", "belum cair").
/// Configured with Lajur:Reports:TimeZone (IANA id, default Asia/Jakarta); server clocks stay UTC.
/// </summary>
public sealed class ReportClock(TimeProvider timeProvider, IConfiguration configuration)
{
    public const string TimeZoneKey = "Lajur:Reports:TimeZone";
    private const string DefaultTimeZone = "Asia/Jakarta";

    private readonly TimeZoneInfo timeZone = Resolve(configuration[TimeZoneKey] ?? DefaultTimeZone);

    public string TimeZoneId => timeZone.Id;

    public DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);

    private static TimeZoneInfo Resolve(string id)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return zone;
        }

        // Minimal containers may lack tzdata; Indonesia has no DST, so a fixed offset is exact for WIB.
        return id == DefaultTimeZone
            ? TimeZoneInfo.CreateCustomTimeZone(DefaultTimeZone, TimeSpan.FromHours(7), "WIB", "WIB")
            : throw new InvalidOperationException($"Report time zone '{id}' is not available on this server.");
    }
}
