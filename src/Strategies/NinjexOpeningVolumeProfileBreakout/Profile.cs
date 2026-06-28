using System;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Ninjex;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
        private NinjexOpeningVolumeProfileEngine profileEngine;

        private bool LoadActiveProfileLevels(DateTime expectedProfileDate)
        {
            if (profileEngine == null || !profileEngine.HasCompletedProfile)
                return false;

            if (profileEngine.LatestProfileDate.Date != expectedProfileDate.Date)
                return false;

            activeVAH = profileEngine.LatestVAH;
            activeVAL = profileEngine.LatestVAL;
            activePOC = profileEngine.LatestPOC;

            return IsValidLevel(activeVAH)
                   && IsValidLevel(activeVAL)
                   && IsValidLevel(activePOC)
                   && activeVAH > activeVAL;
        }
        
        private void ProcessProfileTickForStrategy()
        {
            if (profileEngine == null)
                return;

            if (CurrentBars[1] < 1)
                return;

            var tickChartTime = Times[1][0];

            var profileTime = ConvertChartTimeToEastern
                ? ConvertTime(tickChartTime, sourceTimeZone, easternTimeZone)
                : tickChartTime;

            profileEngine.ProcessTick(
                profileTime,
                Closes[1][0],
                Volumes[1][0],
                TickSize,
                ProfileStartTime,
                ProfileEndTime,
                RowSizeTicks,
                ValueAreaPercent);
        }
        
        private void DrawStrategyProfileLevels()
        {
            if (!ShowProfileHorizontalLines)
                return;

            if (!IsValidLevel(activeVAH) || !IsValidLevel(activeVAL) || !IsValidLevel(activePOC))
                return;

            Draw.HorizontalLine(this, "OVP_STRAT_VAH", activeVAH, System.Windows.Media.Brushes.RoyalBlue);
            Draw.HorizontalLine(this, "OVP_STRAT_VAL", activeVAL, System.Windows.Media.Brushes.RoyalBlue);
            Draw.HorizontalLine(this, "OVP_STRAT_POC", activePOC, System.Windows.Media.Brushes.Red);
        }
    }
}