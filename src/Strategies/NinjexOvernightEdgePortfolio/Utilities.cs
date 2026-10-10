using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        #region Helpers

        private double GetOvernightWidthTicks()
        {
            if (!IsFinite(overnightHigh)
                || !IsFinite(overnightLow)
                || TickSize <= 0)
            {
                return double.NaN;
            }


            return
                (overnightHigh
                 - overnightLow)
                / TickSize;
        }


        private double GetPremarketWidthTicks()
        {
            if (!IsFinite(premarketHigh)
                || !IsFinite(premarketLow)
                || TickSize <= 0)
            {
                return double.NaN;
            }


            return
                (premarketHigh
                 - premarketLow)
                / TickSize;
        }


        private static bool IsEntrySignalName(
            string signalName)
        {
            return
                IsLongEntrySignalName(signalName)
                || IsShortEntrySignalName(signalName);
        }


        private static bool IsLongEntrySignalName(
            string signalName)
        {
            return
                signalName == LongEntrySignal
                || signalName == PriorCloseEntrySignal;
        }


        private static bool IsShortEntrySignalName(
            string signalName)
        {
            return
                signalName == ShortEntrySignal
                || signalName == PremarketHighEntrySignal
                || signalName == RthOpenEntrySignal
                || signalName == PremarketLowEntrySignal;
        }


        private static string GetModelNameForEntrySignal(
            string signalName)
        {
            if (signalName == LongEntrySignal)
                return BaselineLongModelName;

            if (signalName == ShortEntrySignal)
                return BaselineShortModelName;

            if (signalName == PriorCloseEntrySignal)
                return PriorCloseModelName;

            if (signalName == PremarketHighEntrySignal)
                return PremarketHighModelName;

            if (signalName == RthOpenEntrySignal)
                return RthOpenModelName;

            if (signalName == PremarketLowEntrySignal)
                return PremarketLowModelName;


            return "Unknown";
        }


        private string GetActiveEntrySignalForExit(
            PendingDirection direction)
        {
            if (!string.IsNullOrEmpty(activeEntrySignal))
                return activeEntrySignal;


            return
                direction == PendingDirection.Long
                    ? LongEntrySignal
                    : ShortEntrySignal;
        }


        private double GetActiveAverageEntryPrice()
        {
            if (activeEntryFilledQuantity <= 0)
                return double.NaN;


            return
                activeEntryPriceQuantity
                / activeEntryFilledQuantity;
        }


        private static bool IsFinite(
            double value)
        {
            return
                !double.IsNaN(value)
                && !double.IsInfinity(value);
        }


        private static int ToTimeValue(
            DateTime time)
        {
            return
                time.Hour * 10000
                + time.Minute * 100
                + time.Second;
        }


        private static int MinutesBetween(
            int startTime,
            int endTime)
        {
            var startHour =
                startTime / 10000;

            var startMinute =
                (startTime / 100) % 100;

            var endHour =
                endTime / 10000;

            var endMinute =
                (endTime / 100) % 100;


            return
                (endHour * 60 + endMinute)
                -
                (startHour * 60 + startMinute);
        }


        private static DateTime DateTimeForTimeValue(
            DateTime date,
            int timeValue)
        {
            var hour =
                timeValue / 10000;

            var minute =
                (timeValue / 100) % 100;

            var second =
                timeValue % 100;


            return
                date.Date
                    .AddHours(hour)
                    .AddMinutes(minute)
                    .AddSeconds(second);
        }


        private void Diagnostic(
            DateTime time,
            string format,
            params object[] args)
        {
            if (!EnableDiagnostics)
                return;


            var message =
                args == null
                || args.Length == 0
                    ? format
                    : string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        format,
                        args);


            Print(
                string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss.fff} | {1} | {2}",
                    time,
                    Name,
                    message));
        }

        #endregion

    }
}
