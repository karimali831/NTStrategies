#region Using declarations

using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.NinjaScript.DrawingTools;

#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NinjexFairValueGap : Indicator
    {
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Fair Value Gap";
                Description = "Draws simple 3-candle fair value gaps.";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;

                MinGapTicks = 1;
                ShowBullishFvg = true;
                ShowBearishFvg = true;
                ShowLiveCurrentBarFvg = true;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
                return;

            var minGap = MinGapTicks * TickSize;

            // Current live/closed 3-candle FVG:
            // Bullish gap = High[2] to Low[0]
            // Bearish gap = Low[2] to High[0]
            var bullishFvg =
                Low[0] > High[2] &&
                (Low[0] - High[2]) >= minGap;

            var bearishFvg =
                High[0] < Low[2] &&
                (Low[2] - High[0]) >= minGap;

            var bullTag = "BullFVG_" + CurrentBar;
            var bearTag = "BearFVG_" + CurrentBar;

            // If we are drawing live intrabar FVGs, remove the rectangle if price invalidates it.
            // This prevents stale intrabar rectangles from staying on the chart.
            if (ShowLiveCurrentBarFvg)
            {
                if (!bullishFvg)
                    RemoveDrawObject(bullTag);

                if (!bearishFvg)
                    RemoveDrawObject(bearTag);
            }

            if (ShowBullishFvg && bullishFvg)
            {
                Draw.Rectangle(
                    this,
                    bullTag,
                    false,
                    2,
                    High[2],
                    0,
                    Low[0],
                    Brushes.Transparent,
                    Brushes.LimeGreen,
                    20);
            }

            if (ShowBearishFvg && bearishFvg)
            {
                Draw.Rectangle(
                    this,
                    bearTag,
                    false,
                    2,
                    Low[2],
                    0,
                    High[0],
                    Brushes.Transparent,
                    Brushes.Red,
                    20);
            }
        }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Min Gap Ticks", GroupName = "FVG", Order = 1)]
        public int MinGapTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Bullish FVG", GroupName = "FVG", Order = 2)]
        public bool ShowBullishFvg { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Bearish FVG", GroupName = "FVG", Order = 3)]
        public bool ShowBearishFvg { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Live Current Bar FVG", GroupName = "FVG", Order = 4)]
        public bool ShowLiveCurrentBarFvg { get; set; }
    }
}