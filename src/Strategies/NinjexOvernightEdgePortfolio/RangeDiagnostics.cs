using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        private DateTime rangeDiagLastContextBarTime = Core.Globals.MinDate;
        private DateTime rangeDiagSessionDate = Core.Globals.MinDate;
        private int rangeDiagBarsAfterOvernightStart;

        private void DiagnoseContextRangeBar(
            DateTime barTime,
            double high,
            double low)
        {
            if (!EnableDiagnostics)
                return;

            if (rangeDiagLastContextBarTime != Core.Globals.MinDate)
            {
                var delta = barTime - rangeDiagLastContextBarTime;

                if (delta.TotalMinutes < 0 || delta.TotalMinutes > 5.1)
                {
                    Diagnostic(
                        barTime,
                        "RANGE CONTEXT GAP Prev={0:yyyy-MM-dd HH:mm:ss.fff} " +
                        "Current={1:yyyy-MM-dd HH:mm:ss.fff} DeltaMinutes={2:0.###} State={3}",
                        rangeDiagLastContextBarTime,
                        barTime,
                        delta.TotalMinutes,
                        State);
                }
            }

            rangeDiagLastContextBarTime = barTime;

            var timeValue = ToTimeValue(barTime);
            var overnightStart = OvernightStartTime;
            var open = MarketOpenTime;

            // Track the first completed 5-minute bars after the configured
            // overnight start. This is deliberately observational only and is
            // useful for Playback startup/DST diagnostics without altering the
            // authoritative range engine.
            if (timeValue > overnightStart)
            {
                var derivedRangeDate = barTime.Date.AddDays(1);

                if (rangeDiagSessionDate != derivedRangeDate)
                {
                    rangeDiagSessionDate = derivedRangeDate;
                    rangeDiagBarsAfterOvernightStart = 0;
                }

                if (rangeDiagBarsAfterOvernightStart < 18)
                {
                    rangeDiagBarsAfterOvernightStart++;

                    Diagnostic(
                        barTime,
                        "RANGE CONTEXT BAR Phase=OvernightStart " +
                        "Seq={0}/18 BarTime={1:yyyy-MM-dd HH:mm:ss.fff} " +
                        "DerivedRangeDate={2:yyyy-MM-dd} High={3} Low={4} " +
                        "State={5}",
                        rangeDiagBarsAfterOvernightStart,
                        barTime,
                        derivedRangeDate,
                        high,
                        low,
                        State);
                }
            }

            // Also expose the bars immediately around the RTH finalisation point
            // so we can reconcile the engine's final RangeBarCount with the raw
            // primary 5-minute series.
            var minutesFromOpen = MinutesBetween(overnightStart, timeValue);

            if ((timeValue >= 92000 && timeValue <= 93500)
                || (timeValue >= 174500 && timeValue <= 191500))
            {
                Diagnostic(
                    barTime,
                    "RANGE CONTEXT RAW BarTime={0:yyyy-MM-dd HH:mm:ss.fff} " +
                    "TimeValue={1} High={2} Low={3} " +
                    "OvernightActiveDate={4:yyyy-MM-dd} OvernightBars={5} " +
                    "PremarketActiveDate={6:yyyy-MM-dd} PremarketBars={7} State={8}",
                    barTime,
                    timeValue,
                    high,
                    low,
                    overnightRangeEngine == null
                        ? Core.Globals.MinDate
                        : overnightRangeEngine.ActiveRangeDate,
                    overnightRangeEngine == null
                        ? -1
                        : overnightRangeEngine.RangeBarCount,
                    premarketRangeEngine == null
                        ? Core.Globals.MinDate
                        : premarketRangeEngine.ActiveRangeDate,
                    premarketRangeEngine == null
                        ? -1
                        : premarketRangeEngine.RangeBarCount,
                    State);
            }
        }
    }
}
