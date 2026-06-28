using NinjaTrader.NinjaScript.Indicators;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
        private NinjexOpeningVolumeProfile CreateOpeningProfileIndicator()
        {
            return new NinjexOpeningVolumeProfile
            {
                ProfileStartTime = ProfileStartTime,
                ProfileEndTime = ProfileEndTime,
                RowSizeTicks = RowSizeTicks,
                ValueAreaPercent = ValueAreaPercent,
                UseTickDataForProfile = UseTickDataForProfile,
                ConvertChartTimeToEastern = ConvertChartTimeToEastern,
                SourceTimeZoneId = SourceTimeZoneId,
                ShowPanel = ShowProfilePanel,
                ShowHorizontalLines = ShowProfileHorizontalLines,
                ShowVAH = true,
                ShowVAL = true,
                ShowPOC = true
            };
        }
        
        private bool LoadActiveProfileLevels()
        {
            if (openingProfile == null)
                return false;

            activeVAH = openingProfile.VAH[0];
            activeVAL = openingProfile.VAL[0];
            activePOC = openingProfile.POC[0];

            return IsValidLevel(activeVAH)
                   && IsValidLevel(activeVAL)
                   && activeVAH > activeVAL;
        }

    }
}