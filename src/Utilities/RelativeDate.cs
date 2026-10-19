using Prowl.Rosetta;

namespace Prowl.Launcher;

internal static class RelativeDate
{
    internal static string Format(DateOnly date, DateOnly? today = null)
    {
        int days = (today ?? DateOnly.FromDateTime(DateTime.Today)).DayNumber - date.DayNumber;
        return days switch
        {
            0 => Loc.Get("launcher.news.today"),
            1 => Loc.Get("launcher.news.yesterday"),
            -1 => Loc.Get("launcher.news.tomorrow"),
            < 0 => Loc.Get("launcher.news.in_days", new { count = -days }),
            _ => Loc.Get("launcher.news.days_ago", new { count = days })
        };
    }

    /// <summary>Coarse elapsed time since <paramref name="time" />: just now, minutes, hours or days ago.</summary>
    internal static string FormatElapsed(DateTimeOffset time)
    {
        TimeSpan elapsed = DateTimeOffset.UtcNow - time;
        if (elapsed.TotalMinutes < 1)
        {
            return Loc.Get("launcher.projects.just_now");
        }

        if (elapsed.TotalHours < 1)
        {
            return Loc.Get("launcher.projects.minutes_ago", new { count = (int)elapsed.TotalMinutes });
        }

        if (elapsed.TotalDays < 1)
        {
            return Loc.Get("launcher.projects.hours_ago", new { count = (int)elapsed.TotalHours });
        }

        return Loc.Get("launcher.projects.days_ago", new { count = (int)elapsed.TotalDays });
    }
}
