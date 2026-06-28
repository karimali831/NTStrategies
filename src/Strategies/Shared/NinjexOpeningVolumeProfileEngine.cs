using System;
using System.Collections.Generic;
using System.Linq;

namespace NinjaTrader.NinjaScript.Ninjex
{
    public sealed class NinjexOpeningVolumeProfileEngine
    {
        private readonly Dictionary<int, double> volumeByBucket = new Dictionary<int, double>();

        private DateTime activeProfileDate = Core.Globals.MinDate;
        private DateTime completedProfileDate = Core.Globals.MinDate;

        private bool activeProfileFinalized;

        private double latestVAH = double.NaN;
        private double latestVAL = double.NaN;
        private double latestPOC = double.NaN;

        public double LatestVAH => latestVAH;
        public double LatestVAL => latestVAL;
        public double LatestPOC => latestPOC;
        public DateTime LatestProfileDate => completedProfileDate;

        public bool HasCompletedProfile =>
            IsValidLevel(latestVAH)
            && IsValidLevel(latestVAL)
            && IsValidLevel(latestPOC)
            && latestVAH > latestVAL;

        public void Reset()
        {
            volumeByBucket.Clear();

            activeProfileDate = Core.Globals.MinDate;
            completedProfileDate = Core.Globals.MinDate;

            activeProfileFinalized = false;

            latestVAH = double.NaN;
            latestVAL = double.NaN;
            latestPOC = double.NaN;
        }

        public bool ProcessTick(
            DateTime profileTime,
            double price,
            double volume,
            double tickSize,
            int profileStartTime,
            int profileEndTime,
            int rowSizeTicks,
            int valueAreaPercent)
        {
            if (price <= 0 || volume <= 0 || tickSize <= 0)
                return false;

            DateTime profileDate = profileTime.Date;

            if (activeProfileDate != profileDate)
                StartNewProfile(profileDate);

            if (activeProfileFinalized)
                return false;

            var timeValue = ToTime(profileTime);
            var startTime = NormalizeTimeInput(profileStartTime);
            var endTime = NormalizeTimeInput(profileEndTime);

            if (timeValue >= startTime && timeValue < endTime)
            {
                AddVolumeAtPrice(price, volume, tickSize, rowSizeTicks);
                return false;
            }

            if (timeValue >= endTime && volumeByBucket.Count > 0)
            {
                FinalizeProfile(tickSize, rowSizeTicks, valueAreaPercent);

                completedProfileDate = activeProfileDate;
                activeProfileFinalized = true;

                return true;
            }

            return false;
        }

        private void StartNewProfile(DateTime profileDate)
        {
            activeProfileDate = profileDate;
            activeProfileFinalized = false;

            volumeByBucket.Clear();
        }

        private void AddVolumeAtPrice(double price, double volume, double tickSize, int rowSizeTicks)
        {
            var safeRowSizeTicks = Math.Max(1, rowSizeTicks);
            var bucketSize = tickSize * safeRowSizeTicks;

            var bucket = (int)Math.Round(price / bucketSize, MidpointRounding.AwayFromZero);

            if (!volumeByBucket.ContainsKey(bucket))
                volumeByBucket[bucket] = 0;

            volumeByBucket[bucket] += volume;
        }

        private void FinalizeProfile(double tickSize, int rowSizeTicks, int valueAreaPercent)
        {
            if (volumeByBucket.Count == 0)
                return;

            var safeRowSizeTicks = Math.Max(1, rowSizeTicks);
            var bucketSize = tickSize * safeRowSizeTicks;

            var pocBucket = volumeByBucket
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .First()
                .Key;

            var totalVolume = volumeByBucket.Values.Sum();
            var safeValueArea = Math.Max(1, Math.Min(100, valueAreaPercent));
            var targetVolume = totalVolume * (safeValueArea / 100.0);

            var sortedBuckets = volumeByBucket.Keys.OrderBy(x => x).ToList();

            var pocIndex = sortedBuckets.IndexOf(pocBucket);
            var lowerIndex = pocIndex;
            var upperIndex = pocIndex;

            var accumulatedVolume = volumeByBucket[pocBucket];

            while (accumulatedVolume < targetVolume && (lowerIndex > 0 || upperIndex < sortedBuckets.Count - 1))
            {
                var lowerVolume = lowerIndex > 0
                    ? volumeByBucket[sortedBuckets[lowerIndex - 1]]
                    : -1;

                var upperVolume = upperIndex < sortedBuckets.Count - 1
                    ? volumeByBucket[sortedBuckets[upperIndex + 1]]
                    : -1;

                if (upperVolume >= lowerVolume && upperIndex < sortedBuckets.Count - 1)
                {
                    upperIndex++;
                    accumulatedVolume += Math.Max(0, upperVolume);
                }
                else if (lowerIndex > 0)
                {
                    lowerIndex--;
                    accumulatedVolume += Math.Max(0, lowerVolume);
                }
                else
                {
                    break;
                }
            }

            latestPOC = RoundToTick(pocBucket * bucketSize, tickSize);
            latestVAH = RoundToTick(sortedBuckets[upperIndex] * bucketSize, tickSize);
            latestVAL = RoundToTick(sortedBuckets[lowerIndex] * bucketSize, tickSize);
        }

        private static double RoundToTick(double price, double tickSize)
        {
            if (tickSize <= 0)
                return price;

            return Math.Round(price / tickSize, MidpointRounding.AwayFromZero) * tickSize;
        }

        private static int NormalizeTimeInput(int value)
        {
            if (value > 0 && value < 2400)
                return value * 100;

            return value;
        }

        private static int ToTime(DateTime time)
        {
            return time.Hour * 10000 + time.Minute * 100 + time.Second;
        }

        private static bool IsValidLevel(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }
    }
}