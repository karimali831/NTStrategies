using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeFvgBreakout : Strategy
    {
        private DateTime ToEastern(DateTime chartTime)
        {
            if (!ConvertChartTimeToEastern)
                return chartTime;

            try
            {
                var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                return TimeZoneInfo.ConvertTime(chartTime, TimeZoneInfo.Local, eastern);
            }
            catch
            {
                return chartTime;
            }
        }

        private TimeSpan ToTimeSpan(int hhmmss)
        {
            var hours = hhmmss / 10000;
            var minutes = (hhmmss % 10000) / 100;
            var seconds = hhmmss % 100;

            return new TimeSpan(hours, minutes, seconds);
        }
    }
}