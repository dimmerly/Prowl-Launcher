using Prowl.Rosetta;

namespace Prowl.Launcher;

static class RelativeDate
{
    internal static string Format(DateOnly date, DateOnly? today = null)
    {
        int days = (today ?? DateOnly.FromDateTime(DateTime.Today)).DayNumber - date.DayNumber;
        return days switch
        {
            0 => Loc.Get("launcher.news.today"),
            1 => Loc.Get("launcher.news.yesterday"),
            -1 => Loc.Get("launcher.news.tomorrow"),
            < 0 => Loc.Get("launcher.news.in_days", new
            {
                count = -days
            }),
            _ => Loc.Get("launcher.news.days_ago", new
            {
                count = days
            })
        };
    }
}
