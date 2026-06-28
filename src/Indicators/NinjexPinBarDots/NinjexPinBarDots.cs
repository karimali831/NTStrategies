#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NinjexPinBarDots : Indicator
    {
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "Ninjex Pin Bar Dots";
                Description = "Draws dots above/below pin bars based on body and wick size filters.";

                Calculate   = Calculate.OnBarClose;
                IsOverlay   = true;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;

                EnableLongPinBars  = true;
                EnableShortPinBars = true;

                LongMinBodyTicks = 0;
                LongMaxBodyTicks = 20;
                LongMinWickTicks = 20;
                LongMaxWickTicks = 0;

                ShortMinBodyTicks = 0;
                ShortMaxBodyTicks = 20;
                ShortMinWickTicks = 20;
                ShortMaxWickTicks = 0;

                DotOffsetTicks = 2;
				
				LongMaxOppositeWickTicks = 8;
				ShortMaxOppositeWickTicks = 8;

                LongDotBrush  = Brushes.LimeGreen;
                ShortDotBrush = Brushes.Red;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;

            var bodyTicks = Math.Abs(Close[0] - Open[0]) / TickSize;

            var upperWickTicks = (High[0] - Math.Max(Open[0], Close[0])) / TickSize;
            var lowerWickTicks = (Math.Min(Open[0], Close[0]) - Low[0]) / TickSize;

			if (EnableLongPinBars &&
			    PassesFilter(bodyTicks, LongMinBodyTicks, LongMaxBodyTicks) &&
			    PassesFilter(lowerWickTicks, LongMinWickTicks, LongMaxWickTicks) &&
			    PassesMaxFilter(upperWickTicks, LongMaxOppositeWickTicks))
			{
			    Draw.Dot(
			        this,
			        "LongPinBarDot_" + CurrentBar,
			        false,
			        0,
			        Low[0] - DotOffsetTicks * TickSize,
			        LongDotBrush);
			}
			
			if (EnableShortPinBars &&
			    PassesFilter(bodyTicks, ShortMinBodyTicks, ShortMaxBodyTicks) &&
			    PassesFilter(upperWickTicks, ShortMinWickTicks, ShortMaxWickTicks) &&
			    PassesMaxFilter(lowerWickTicks, ShortMaxOppositeWickTicks))
			{
			    Draw.Dot(
			        this,
			        "ShortPinBarDot_" + CurrentBar,
			        false,
			        0,
			        High[0] + DotOffsetTicks * TickSize,
			        ShortDotBrush);
			}
        }
		
		private bool PassesMaxFilter(double valueTicks, int maxTicks)
		{
		    if (maxTicks > 0 && valueTicks > maxTicks)
		        return false;
		
		    return true;
		}

        private bool PassesFilter(double valueTicks, int minTicks, int maxTicks)
        {
            if (minTicks > 0 && valueTicks < minTicks)
                return false;

            if (maxTicks > 0 && valueTicks > maxTicks)
                return false;

            return true;
        }

        #region Parameters

        [NinjaScriptProperty]
        [Display(Name = "Enable Long Pin Bars", GroupName = "Long Pin Bars", Order = 0)]
        public bool EnableLongPinBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Long Min Body Ticks", GroupName = "Long Pin Bars", Order = 1)]
        public int LongMinBodyTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Long Max Body Ticks", GroupName = "Long Pin Bars", Order = 2)]
        public int LongMaxBodyTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Long Min Lower Wick Ticks", GroupName = "Long Pin Bars", Order = 3)]
        public int LongMinWickTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Long Max Lower Wick Ticks", GroupName = "Long Pin Bars", Order = 4)]
        public int LongMaxWickTicks { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Enable Short Pin Bars", GroupName = "Short Pin Bars", Order = 0)]
        public bool EnableShortPinBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Short Min Body Ticks", GroupName = "Short Pin Bars", Order = 1)]
        public int ShortMinBodyTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Short Max Body Ticks", GroupName = "Short Pin Bars", Order = 2)]
        public int ShortMaxBodyTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Short Min Upper Wick Ticks", GroupName = "Short Pin Bars", Order = 3)]
        public int ShortMinWickTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Short Max Upper Wick Ticks", GroupName = "Short Pin Bars", Order = 4)]
        public int ShortMaxWickTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Dot Offset Ticks", GroupName = "Visual", Order = 0)]
        public int DotOffsetTicks { get; set; }

        [XmlIgnore]
        [Display(Name = "Long Dot Brush", GroupName = "Visual", Order = 1)]
        public Brush LongDotBrush { get; set; }

        [Browsable(false)]
        public string LongDotBrushSerializable
        {
            get { return Serialize.BrushToString(LongDotBrush); }
            set { LongDotBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Short Dot Brush", GroupName = "Visual", Order = 2)]
        public Brush ShortDotBrush { get; set; }

        [Browsable(false)]
        public string ShortDotBrushSerializable
        {
            get { return Serialize.BrushToString(ShortDotBrush); }
            set { ShortDotBrush = Serialize.StringToBrush(value); }
        }
		
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Long Max Opposite Upper Wick Ticks", GroupName = "Long Pin Bars", Order = 5)]
		public int LongMaxOppositeWickTicks { get; set; }
		
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Short Max Opposite Lower Wick Ticks", GroupName = "Short Pin Bars", Order = 5)]
		public int ShortMaxOppositeWickTicks { get; set; }

        #endregion
    }
}