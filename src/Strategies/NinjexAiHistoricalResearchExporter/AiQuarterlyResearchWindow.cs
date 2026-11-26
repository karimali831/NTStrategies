using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Research front-month calendar. Based on CME's customary US index roll
    /// Monday of the third-Friday expiry week, with the new contract active
    /// from the preceding Sunday ETH open (18:00 ET). No price adjustment,
    /// merging or live trading. These are RESEARCH window conventions, not an
    /// assertion that every institution rolls on that exact date.
    /// </summary>
    internal static class AiQuarterlyResearchWindow
    {
        internal sealed class Window
        {
            public string Contract { get; set; }
            public DateTime FirstEasternDate { get; set; }
            public DateTime LastEasternDate { get; set; }
            public DateTime PreviousQuarterRollMonday { get; set; }
            public DateTime CurrentQuarterRollMonday { get; set; }
        }

        private static readonly Regex ContractPattern = new Regex(
            @"^(ES|NQ)\s+(03|06|09|12)-(\d{2})$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static DateTime ThirdFriday(DateTime monthDate)
        {
            var month = new DateTime(monthDate.Year, monthDate.Month, 1);
            var offset = ((int)DayOfWeek.Friday - (int)month.DayOfWeek + 7) % 7;
            return month.AddDays(offset + 14);
        }

        public static DateTime RollMonday(DateTime expiryMonth)
        {
            return ThirdFriday(expiryMonth).AddDays(-4);
        }

        public static Window Resolve(string instrument, DateTime globalFromEt,
            DateTime globalThroughEt)
        {
            var match = ContractPattern.Match((instrument ?? "").Trim());
            if (!match.Success)
                throw new ArgumentException("Batch mode expects dated ES/NQ contract e.g. ES 12-23.");
            var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var year = 2000 + int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            var expiry = new DateTime(year, month, 1);
            var previous = expiry.AddMonths(-3);
            var first = RollMonday(previous).AddDays(-1);
            var last = RollMonday(expiry).AddDays(-3);
            // User-supplied global research bounds take priority.
            if (first < globalFromEt.Date) first = globalFromEt.Date;
            if (last > globalThroughEt.Date) last = globalThroughEt.Date;
            if (last < first)
                throw new ArgumentException("No overlap between batch research window and " + instrument);

            return new Window
            {
                Contract = instrument.Trim(),
                FirstEasternDate = first,
                LastEasternDate = last,
                PreviousQuarterRollMonday = RollMonday(previous),
                CurrentQuarterRollMonday = RollMonday(expiry)
            };
        }
    }
}
