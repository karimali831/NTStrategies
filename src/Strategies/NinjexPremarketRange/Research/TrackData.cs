using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch : Strategy
    {
        private void TrackFiveMinuteData(DateTime time)
        {
            if (sessionQuality == null)
                return;
            var value = ToTime(time);
            if (value > RangeStartTime && value <= MarketOpenTime)
            {
                sessionQuality.FiveMinuteRangeBarCount++;
                sessionQuality.HasFiveMinuteData = true;
                if (sessionQuality.FirstFiveMinuteBarTime == DateTime.MinValue)
                    sessionQuality.FirstFiveMinuteBarTime = time;
                sessionQuality.LastFiveMinuteBarTime = time;
            }
        }

        private void TrackOneMinuteData(DateTime time)
        {
            if (sessionQuality == null || !IsInsideEntryWindow(time))
                return;
            sessionQuality.OneMinuteEntryWindowBarCount++;
            sessionQuality.HasOneMinuteData = true;
            if (sessionQuality.FirstOneMinuteBarTime == DateTime.MinValue)
                sessionQuality.FirstOneMinuteBarTime = time;
            sessionQuality.LastOneMinuteBarTime = time;
        }

        private void TrackTickData(DateTime time)
        {
            if (sessionQuality == null || !IsInsideEntryWindow(time))
                return;
            sessionQuality.TickCount++;
            sessionQuality.HasTickData = true;
            if (sessionQuality.FirstTickTime == DateTime.MinValue)
                sessionQuality.FirstTickTime = time;
            sessionQuality.LastTickTime = time;
        }

    }
}