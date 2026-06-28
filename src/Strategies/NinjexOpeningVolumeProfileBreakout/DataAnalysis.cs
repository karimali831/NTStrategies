using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
        private string dataFilePath = string.Empty;
        private string runId = string.Empty;
        private DateTime lastProfileLoggedDate = Core.Globals.MinDate;
        
        private void LogDailyProfileIfNeeded(DateTime easternNow)
        {
            if (!EnableDataCollection || !LogDailyProfileRows)
                return;

            if (string.IsNullOrWhiteSpace(dataFilePath))
                return;

            var profileDate = easternNow.Date;

            if (lastProfileLoggedDate == profileDate)
                return;

            if (!IsValidLevel(activeVAH) || !IsValidLevel(activeVAL))
                return;

            lastProfileLoggedDate = profileDate;

            AppendDataRow(
                eventType: "PROFILE",
                decision: "Completed",
                dateEt: profileDate,
                timeChart: Time[0],
                timeEt: easternNow,
                direction: "",
                open: double.NaN,
                high: double.NaN,
                low: double.NaN,
                close: double.NaN,
                bodyHigh: double.NaN,
                bodyLow: double.NaN,
                entryPrice: double.NaN,
                entryDistanceTicks: double.NaN,
                entryDistancePoints: double.NaN,
                stopPrice: double.NaN,
                targetPrice: double.NaN,
                barsTracked: 0,
                mfeUsd: 0,
                maeUsd: 0,
                realizedPnlUsd: double.NaN,
                outcome: "",
                exitTimeChart: Core.Globals.MinDate,
                exitPrice: double.NaN,
                notes: "Daily profile levels");
        }

        private void LogRejectedSetup(string direction, string reason, double bodyHigh, double bodyLow)
        {
            if (!EnableDataCollection || !LogRejectedSetups)
                return;

            var easternNow = ConvertChartTimeToEastern
                ? ConvertTime(Time[0], sourceTimeZone, easternTimeZone)
                : Time[0];
            
            var entryDistanceTicks = GetEntryDistanceTicks(direction, Close[0]);
            var entryDistancePoints = entryDistanceTicks * TickSize;

            AppendDataRow(
                eventType: "REJECTED_SETUP",
                decision: reason,
                dateEt: easternNow.Date,
                timeChart: Time[0],
                timeEt: easternNow,
                direction: direction,
                open: Open[0],
                high: High[0],
                low: Low[0],
                close: Close[0],
                bodyHigh: bodyHigh,
                bodyLow: bodyLow,
                entryPrice: Close[0],
                entryDistanceTicks,
                entryDistancePoints,
                stopPrice: double.NaN,
                targetPrice: double.NaN,
                barsTracked: 0,
                mfeUsd: 0,
                maeUsd: 0,
                realizedPnlUsd: double.NaN,
                outcome: "Rejected",
                exitTimeChart: Core.Globals.MinDate,
                exitPrice: double.NaN,
                notes: reason);
        }
    }
}