#region Using declarations

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using NinjaTrader.Gui.Chart;
using SharpDX;
using SharpDX.DirectWrite;

using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using DxBrush = SharpDX.Direct2D1.Brush;
using DxSolidColorBrush = SharpDX.Direct2D1.SolidColorBrush;

#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NinjexVolumetricFVG : Indicator
    {
        private sealed class FvgZone
        {
            public int StartBar;
            public int EndBar;

            public double Top;
            public double Bottom;

            public bool IsBullish;
            public bool Active = true;

            public double BullVolume;
            public double BearVolume;
            public double TotalVolume;

            public double BullPercent;
            public double BearPercent;
        }

        private readonly List<FvgZone> zones = new List<FvgZone>();
        private TextFormat textFormat;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Volumetric FVG";
                Description = "Volumetric Fair Value Gap indicator with bullish/bearish volume split.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;

                ShowBullish = true;
                ShowBearish = true;

                MinGapTicks = 4;
                MaxZones = 40;

                InvalidateByWick = false;
                HideInvalidatedZones = true;

                ShowVolumeBars = true;
                ShowVolumeText = true;

                VolumeBarWidthPercent = 55;
                FontSize = 12;

                BullishGapBrush = new WpfSolidColorBrush(WpfColor.FromArgb(55, 0, 180, 160));
                BearishGapBrush = new WpfSolidColorBrush(WpfColor.FromArgb(55, 240, 60, 85));

                BullVolumeBrush = new WpfSolidColorBrush(WpfColor.FromArgb(120, 0, 180, 160));
                BearVolumeBrush = new WpfSolidColorBrush(WpfColor.FromArgb(120, 240, 60, 85));

                InvalidatedBrush = new WpfSolidColorBrush(WpfColor.FromArgb(35, 130, 130, 130));
                BorderBrush = new WpfSolidColorBrush(WpfColor.FromArgb(80, 255, 255, 255));
                TextBrush = WpfBrushes.White;
            }
            else if (State == State.DataLoaded)
            {
                zones.Clear();
            }
            else if (State == State.Terminated)
            {
                if (textFormat != null)
                {
                    textFormat.Dispose();
                    textFormat = null;
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
                return;

            UpdateExistingZones();

            bool bullishFvg = High[2] < Low[0];
            bool bearishFvg = Low[2] > High[0];

            if (ShowBullish && bullishFvg)
            {
                double top = Low[0];
                double bottom = High[2];

                if (GetGapSizeTicks(top, bottom) >= MinGapTicks)
                    CreateZone(true, top, bottom);
            }

            if (ShowBearish && bearishFvg)
            {
                double top = Low[2];
                double bottom = High[0];

                if (GetGapSizeTicks(top, bottom) >= MinGapTicks)
                    CreateZone(false, top, bottom);
            }

            TrimOldZones();
        }

        private void UpdateExistingZones()
        {
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                FvgZone zone = zones[i];

                if (!zone.Active)
                    continue;

                bool invalidated;

                if (zone.IsBullish)
                {
                    invalidated = InvalidateByWick
                        ? Low[0] < zone.Bottom
                        : Close[0] < zone.Bottom;
                }
                else
                {
                    invalidated = InvalidateByWick
                        ? High[0] > zone.Top
                        : Close[0] > zone.Top;
                }

                if (invalidated)
                {
                    zone.Active = false;
                    zone.EndBar = CurrentBar;

                    if (HideInvalidatedZones)
                        zones.RemoveAt(i);
                }
                else
                {
                    zone.EndBar = CurrentBar;
                }
            }
        }

        private void CreateZone(bool isBullish, double top, double bottom)
        {
            double bullVolume = 0;
            double bearVolume = 0;

            for (int barsAgo = 0; barsAgo <= 2; barsAgo++)
            {
                if (Close[barsAgo] >= Open[barsAgo])
                    bullVolume += Volume[barsAgo];
                else
                    bearVolume += Volume[barsAgo];
            }

            double totalVolume = bullVolume + bearVolume;
            double bullPercent = totalVolume > 0 ? bullVolume / totalVolume : 0;
            double bearPercent = totalVolume > 0 ? bearVolume / totalVolume : 0;

            zones.Add(new FvgZone
            {
                StartBar = CurrentBar - 2,
                EndBar = CurrentBar,
                Top = top,
                Bottom = bottom,
                IsBullish = isBullish,
                Active = true,
                BullVolume = bullVolume,
                BearVolume = bearVolume,
                TotalVolume = totalVolume,
                BullPercent = bullPercent,
                BearPercent = bearPercent
            });
        }

        private void TrimOldZones()
        {
            while (zones.Count > MaxZones)
                zones.RemoveAt(0);
        }

        private double GetGapSizeTicks(double top, double bottom)
        {
            return Math.Abs(top - bottom) / TickSize;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (ChartBars == null || chartControl == null || chartScale == null || zones.Count == 0)
                return;

            if (textFormat == null)
                textFormat = new TextFormat(Core.Globals.DirectWriteFactory, "Arial", FontSize);

            using (DxBrush bullishGapBrushDx = CreateDxBrush(BullishGapBrush))
            using (DxBrush bearishGapBrushDx = CreateDxBrush(BearishGapBrush))
            using (DxBrush bullVolumeBrushDx = CreateDxBrush(BullVolumeBrush))
            using (DxBrush bearVolumeBrushDx = CreateDxBrush(BearVolumeBrush))
            using (DxBrush invalidatedBrushDx = CreateDxBrush(InvalidatedBrush))
            using (DxBrush borderBrushDx = CreateDxBrush(BorderBrush))
            using (DxBrush textBrushDx = CreateDxBrush(TextBrush))
            {
                foreach (FvgZone zone in zones)
                {
                    if (zone.Top <= zone.Bottom)
                        continue;

                    int visibleStart = ChartBars.FromIndex;
                    int visibleEnd = ChartBars.ToIndex;

                    if (zone.EndBar < visibleStart || zone.StartBar > visibleEnd)
                        continue;

                    int leftBar = Math.Max(zone.StartBar, visibleStart);
                    float x1 = chartControl.GetXByBarIndex(ChartBars, leftBar);

                    float x2;

                    if (zone.Active)
                    {
                        x2 = (float)(ChartPanel.X + ChartPanel.W);
                    }
                    else
                    {
                        int rightBar = Math.Min(zone.EndBar, visibleEnd);
                        x2 = chartControl.GetXByBarIndex(ChartBars, rightBar);
                    }

                    if (x2 <= x1)
                        continue;

                    float yTop = chartScale.GetYByValue(zone.Top);
                    float yBottom = chartScale.GetYByValue(zone.Bottom);

                    if (yBottom <= yTop)
                        continue;

                    var zoneRect = new RectangleF(x1, yTop, x2 - x1, yBottom - yTop);

                    DxBrush gapBrush = zone.Active
                        ? zone.IsBullish ? bullishGapBrushDx : bearishGapBrushDx
                        : invalidatedBrushDx;

                    RenderTarget.FillRectangle(zoneRect, gapBrush);
                    RenderTarget.DrawRectangle(zoneRect, borderBrushDx, 1f);

                    if (ShowVolumeBars && zone.Active)
                        DrawVolumeBars(zone, x1, x2, yTop, yBottom, bullVolumeBrushDx, bearVolumeBrushDx);

                    if (ShowVolumeText)
                        DrawVolumeText(zone, x1, x2, yTop, yBottom, textBrushDx);
                }
            }
        }

        private DxBrush CreateDxBrush(WpfBrush brush)
        {
            WpfColor color = WpfColors.White;

            WpfSolidColorBrush solid = brush as WpfSolidColorBrush;

            if (solid != null)
                color = solid.Color;

            return new DxSolidColorBrush(
                RenderTarget,
                new Color4(
                    color.R / 255f,
                    color.G / 255f,
                    color.B / 255f,
                    color.A / 255f));
        }

        private void DrawVolumeBars(
            FvgZone zone,
            float x1,
            float x2,
            float yTop,
            float yBottom,
            DxBrush bullVolumeBrushDx,
            DxBrush bearVolumeBrushDx)
        {
            float width = x2 - x1;
            float height = yBottom - yTop;

            if (width <= 0 || height <= 0)
                return;

            float maxVolumeWidth = width * (VolumeBarWidthPercent / 100f);
            float midY = yTop + height * 0.5f;

            float bullWidth = maxVolumeWidth * (float)zone.BullPercent;
            float bearWidth = maxVolumeWidth * (float)zone.BearPercent;

            var bullRect = new RectangleF(
                x1,
                yTop,
                Math.Max(1, bullWidth),
                Math.Max(1, midY - yTop));

            var bearRect = new RectangleF(
                x1,
                midY,
                Math.Max(1, bearWidth),
                Math.Max(1, yBottom - midY));

            RenderTarget.FillRectangle(bullRect, bullVolumeBrushDx);
            RenderTarget.FillRectangle(bearRect, bearVolumeBrushDx);
        }

        private void DrawVolumeText(
            FvgZone zone,
            float x1,
            float x2,
            float yTop,
            float yBottom,
            DxBrush textBrushDx)
        {
            float width = x2 - x1;
            float height = yBottom - yTop;

            if (width <= 40 || height <= 8)
                return;

            string text = FormatVolume(zone.TotalVolume);

            if (ShowVolumeBars && zone.TotalVolume > 0)
                text += string.Format("  B {0:0}% / S {1:0}%", zone.BullPercent * 100, zone.BearPercent * 100);

            float textWidth = Math.Min(260, width - 8);
            float textHeight = Math.Min(36, Math.Max(14, height));

            var textRect = new RectangleF(
                Math.Max(x1 + 4, x2 - textWidth - 8),
                yTop + (height - textHeight) * 0.5f,
                textWidth,
                textHeight);

            RenderTarget.DrawText(text, textFormat, textRect, textBrushDx);
        }

        private string FormatVolume(double value)
        {
            if (value >= 1000000)
                return (value / 1000000d).ToString("0.##") + "M";

            if (value >= 1000)
                return (value / 1000d).ToString("0.##") + "K";

            return value.ToString("0");
        }

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Show Bullish FVGs", GroupName = "Parameters", Order = 1)]
        public bool ShowBullish { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Bearish FVGs", GroupName = "Parameters", Order = 2)]
        public bool ShowBearish { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Minimum Gap Size", Description = "Minimum FVG size in ticks. 0 disables the filter.", GroupName = "Parameters", Order = 3)]
        public int MinGapTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "Max Zones", GroupName = "Parameters", Order = 4)]
        public int MaxZones { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Invalidate By Wick", Description = "False = invalidate by close beyond the zone. True = invalidate by wick beyond the zone.", GroupName = "Parameters", Order = 5)]
        public bool InvalidateByWick { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Hide Invalidated Zones", GroupName = "Parameters", Order = 6)]
        public bool HideInvalidatedZones { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Volume Bars", GroupName = "Visual", Order = 10)]
        public bool ShowVolumeBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Volume Text", GroupName = "Visual", Order = 11)]
        public bool ShowVolumeText { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Volume Bar Width %", GroupName = "Visual", Order = 12)]
        public int VolumeBarWidthPercent { get; set; }

        [NinjaScriptProperty]
        [Range(8, 30)]
        [Display(Name = "Font Size", GroupName = "Visual", Order = 13)]
        public int FontSize { get; set; }

        [XmlIgnore]
        [Display(Name = "Bullish Gap Brush", GroupName = "Colours", Order = 20)]
        public WpfBrush BullishGapBrush { get; set; }

        [XmlIgnore]
        [Display(Name = "Bearish Gap Brush", GroupName = "Colours", Order = 21)]
        public WpfBrush BearishGapBrush { get; set; }

        [XmlIgnore]
        [Display(Name = "Bull Volume Brush", GroupName = "Colours", Order = 22)]
        public WpfBrush BullVolumeBrush { get; set; }

        [XmlIgnore]
        [Display(Name = "Bear Volume Brush", GroupName = "Colours", Order = 23)]
        public WpfBrush BearVolumeBrush { get; set; }

        [XmlIgnore]
        [Display(Name = "Invalidated Brush", GroupName = "Colours", Order = 24)]
        public WpfBrush InvalidatedBrush { get; set; }

        [XmlIgnore]
        [Display(Name = "Border Brush", GroupName = "Colours", Order = 25)]
        public WpfBrush BorderBrush { get; set; }

        [XmlIgnore]
        [Display(Name = "Text Brush", GroupName = "Colours", Order = 26)]
        public WpfBrush TextBrush { get; set; }

        #endregion
    }
}