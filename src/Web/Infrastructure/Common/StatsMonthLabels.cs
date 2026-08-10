using System.Globalization;
using Web.Infrastructure.Models;

namespace Web.Infrastructure.Common;

internal static class StatsMonthLabels
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    public static bool SpansYears(IReadOnlyList<AccountMonthStat> months) =>
        months.Select(item => item.Year).Distinct().Skip(1).Any();

    public static string Format(AccountMonthStat month, bool spansYears)
    {
        var value = new DateTime(month.Year, month.Month, 1);
        return value.ToString(spansYears ? "MMM yyyy" : "MMM", EnUs);
    }
}
