namespace ProjectGuild.View.UI
{
    /// <summary>
    /// Formats elapsed game time as a progressive human-readable clock.
    /// Under 1h: M:SS. Under 24h: H:MM:SS. 24h+: Dd H:MM:SS.
    /// </summary>
    public static class TimeFormatHelper
    {
        public static string FormatElapsedTime(float totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;

            int total = (int)totalSeconds;
            int seconds = total % 60;
            int minutes = (total / 60) % 60;
            int hours = (total / 3600) % 24;
            int days = total / 86400;

            if (days > 0)
                return $"{days}d {hours}:{minutes:D2}:{seconds:D2}";
            if (hours > 0)
                return $"{hours}:{minutes:D2}:{seconds:D2}";
            return $"{minutes}:{seconds:D2}";
        }
    }
}
