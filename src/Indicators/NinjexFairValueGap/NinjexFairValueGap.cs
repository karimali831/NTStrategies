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
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;

                MinGapTicks = 1;
                ShowBullishFvg = true;
                ShowBearishFvg = true;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
                return;

            var minGap = MinGapTicks * TickSize;

            var bullishFvg = Low[0] > High[2] && (Low[0] - High[2]) >= minGap;
            var bearishFvg = High[0] < Low[2] && (Low[2] - High[0]) >= minGap;

            if (ShowBullishFvg && bullishFvg)
            {
                Draw.Rectangle(
                    this,
                    "BullFVG_" + CurrentBar,
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
                    "BearFVG_" + CurrentBar,
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
    }
}