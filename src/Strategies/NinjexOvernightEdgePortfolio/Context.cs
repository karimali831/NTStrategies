using System;
using NinjaTrader.NinjaScript.Ninjex;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        #region 5-minute context / overnight range

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

        #endregion

    }
}
