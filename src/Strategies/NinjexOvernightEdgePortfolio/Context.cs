using System;
using System.Collections.Generic;
using System.Text;
using NinjaTrader.NinjaScript.Ninjex;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        // Diagnostics only: record the completed 5-minute timestamps actually seen
        // by the strategy, independent of the range engine's internal counter.
        private readonly Dictionary<DateTime, HashSet<DateTime>> overnightObservedBars =
            new Dictionary<DateTime, HashSet<DateTime>>();

        private void RecordOvernightCoverageBar(DateTime barTime)
        {
            // NinjaTrader 5-minute bars are end-stamped. The overnight range
            // contains the 186 closes from 18:05 through next day 09:30 ET.
            var rangeDate = barTime.TimeOfDay > TimeSpan.FromHours(18)
                ? barTime.Date.AddDays(1)
                : barTime.Date;
            var start = rangeDate.AddDays(-1).AddHours(18);
            var end = rangeDate.AddHours(9).AddMinutes(30);
            if (barTime <= start || barTime > end ||
                barTime.TimeOfDay.Ticks % TimeSpan.FromMinutes(5).Ticks != 0)
                return;

            HashSet<DateTime> observed;
            if (!overnightObservedBars.TryGetValue(rangeDate, out observed))
            {
                observed = new HashSet<DateTime>();
                overnightObservedBars[rangeDate] = observed;
            }
            observed.Add(barTime);
        }

        private void LogOvernightCoverage(DateTime rangeDate, int engineCount, DateTime timestamp)
        {
            HashSet<DateTime> observed;
            if (!overnightObservedBars.TryGetValue(rangeDate, out observed))
                observed = new HashSet<DateTime>();

            var missing = new List<string>();
            var first = rangeDate.AddDays(-1).AddHours(18).AddMinutes(5);
            var last = rangeDate.AddHours(9).AddMinutes(30);
            for (var t = first; t <= last; t = t.AddMinutes(5))
                if (!observed.Contains(t))
                    missing.Add(t.ToString("MM-dd HH:mm"));

            // Emit once per overnight finalization, including healthy sessions.
            // Comparing ObservedCount to EngineCount also detects bars that
            // reached ProcessContextSeries but were excluded by range logic.
            Diagnostic(timestamp,
                "OVERNIGHT COVERAGE Instrument={0} Date={1:yyyy-MM-dd} " +
                "Expected=186 Observed={2} EngineCount={3} Missing={4} " +
                "MissingBarEndsET=[{5}]",
                Instrument.FullName, rangeDate, observed.Count, engineCount,
                missing.Count, string.Join(",", missing.ToArray()));
            overnightObservedBars.Remove(rangeDate);
        }

        private void ProcessContextSeries()
        {
            if (CurrentBars[ContextSeriesIndex]
                < Math.Max(
                    Math.Max(
                        Math.Max(
                            AtrPeriod,
                            EmaFastPeriod),
                        EmaSlowPeriod),
                    30) + 2)
            {
                return;
            }

            if (!IsFirstTickOfBar)
                return;


            var barTime =
                Times[ContextSeriesIndex][1];

            var high =
                Highs[ContextSeriesIndex][1];

            var low =
                Lows[ContextSeriesIndex][1];

            RecordOvernightCoverageBar(barTime);


            //
            // Build overnight range from completed 5-minute bars.
            //
            var overnightFinalizedNow =
                overnightRangeEngine
                    .ProcessCompletedBar(
                        barTime,
                        high,
                        low,
                        KeyLevelsMode.Overnight,
                        PremarketStartTime,
                        OvernightStartTime,
                        MarketOpenTime);

            var premarketFinalizedNow =
                premarketRangeEngine
                    .ProcessCompletedBar(
                        barTime,
                        high,
                        low,
                        KeyLevelsMode.Premarket,
                        PremarketStartTime,
                        OvernightStartTime,
                        MarketOpenTime);


            if (overnightFinalizedNow
                && overnightRangeEngine.IsRangeComplete)
            {
                overnightRangeDate =
                    overnightRangeEngine
                        .LatestRangeDate;

                overnightHigh =
                    overnightRangeEngine
                        .LatestHigh;

                overnightLow =
                    overnightRangeEngine
                        .LatestLow;

                overnightBars =
                    overnightRangeEngine
                        .RangeBarCount;

                LogOvernightCoverage(overnightRangeDate, overnightBars, barTime);


                var finiteRange =
                    IsFinite(overnightHigh)
                    && IsFinite(overnightLow)
                    && overnightHigh > overnightLow;

                var completeRange =
                    !RequireCompleteOvernightRange
                    || overnightBars >= ExpectedOvernightBars;

                overnightRangeReady =
                    finiteRange
                    && completeRange;


                Diagnostic(
                    barTime,
                    "OVERNIGHT READY " +
                    "Date={0:yyyy-MM-dd} " +
                    "High={1} Low={2} Width={3:0.0}t " +
                    "Bars={4} Complete={5}",
                    overnightRangeDate,
                    overnightHigh,
                    overnightLow,
                    GetOvernightWidthTicks(),
                    overnightBars,
                    overnightRangeReady);
            }


            if (premarketFinalizedNow
                && premarketRangeEngine.IsRangeComplete)
            {
                premarketRangeDate =
                    premarketRangeEngine.LatestRangeDate;

                premarketHigh =
                    premarketRangeEngine.LatestHigh;

                premarketLow =
                    premarketRangeEngine.LatestLow;

                premarketBars =
                    premarketRangeEngine.RangeBarCount;

                premarketRangeReady =
                    IsFinite(premarketHigh)
                    && IsFinite(premarketLow)
                    && premarketHigh > premarketLow
                    && (!RequireCompletePremarketRange
                        || premarketBars
                            >= ExpectedPremarketBars);

                Diagnostic(
                    barTime,
                    "PREMARKET READY " +
                    "Date={0:yyyy-MM-dd} " +
                    "High={1} Low={2} Width={3:0.0}t " +
                    "Bars={4} Complete={5}",
                    premarketRangeDate,
                    premarketHigh,
                    premarketLow,
                    GetPremarketWidthTicks(),
                    premarketBars,
                    premarketRangeReady);
            }


            //
            // Preserve the previous completed EMA values for observational
            // research telemetry before the authoritative context is updated.
            // This does not participate in signal qualification.
            //
            CaptureResearchPreviousFiveMinuteContext(
                last5mEmaFast,
                last5mEmaSlow);

            //
            // Cache exactly the completed 5-minute context.
            // This mirrors the neutral collector: signal rows do not
            // peek into the still-forming 5-minute candle.
            //
            last5mTime =
                barTime;

            last5mAtrTicks =
                atr5m[1] / TickSize;

            last5mEmaFast =
                emaFast5m[1];

            last5mEmaSlow =
                emaSlow5m[1];
        }
    }
}
