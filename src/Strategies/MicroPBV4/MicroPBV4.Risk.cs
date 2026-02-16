#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV4 : Strategy
    {
        private void PrepareBracket(string tag, double atrNow)
        {
            int stopTicks;

            if (StopMultATR <= 0)
            {
                stopTicks = Math.Max(8, MaxStopTicks);
            }
            else
            {
                var atrTicksRaw = (atrNow / TickSize) * StopMultATR;

                stopTicks = Math.Min(
                    MaxStopTicks,
                    Math.Max(8, (int)Math.Round(atrTicksRaw)));
            }

            var targetTicksBase = Math.Max(8, (int)Math.Round(stopTicks * R_Ratio));
            if (MaxProfitTicks > 0)
                targetTicksBase = Math.Min(targetTicksBase, MaxProfitTicks);

            SetStopLoss(tag, CalculationMode.Ticks, stopTicks, false);
            SetProfitTarget(tag, CalculationMode.Ticks, targetTicksBase);
        }

        private void ManageBreakEven()
        {
            if (!UseBreakEven)
                return;

            // Safety: ensure indicator has that bar
            if (CurrentBar < 0)
                return;
            
            // Position
            var longSide = Position.MarketPosition == MarketPosition.Long;

            // Disable BE in strong momentum
            StrongTrend(longSide, out var trendStrengthTicks, out _, out _);

            if (DisableBeAboveAdx > 0 && adx[0] >= DisableBeAboveAdx && trendStrengthTicks >= 200)
            {
                Print("[BE BYPASS] Adx: " + adx[0] + ", TrendStrengthTicks: " + trendStrengthTicks);
                return;
            }

            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            if (string.IsNullOrEmpty(lastEntryTag))
                return;

            if (CurrentBar == lastBEBar)
                return;

            var entryPrice = Position.AveragePrice;
            if (entryPrice <= 0)
                return;

            int upTicks;
            if (longSide)
                upTicks = (int)Math.Floor((Close[0] - entryPrice) / TickSize);
            else
                upTicks = (int)Math.Floor((entryPrice - Close[0]) / TickSize);

            if (upTicks < BE_TriggerTicks)
                return;

            var newStop = longSide
                ? entryPrice + BE_PlusTicks * TickSize
                : entryPrice - BE_PlusTicks * TickSize;

            newStop = Instrument.MasterInstrument.RoundToTickSize(newStop);

            if (Math.Abs(newStop - lastBEPrice) >= TickSize)
            {
                try
                {
                    if (DebugMode)
                    {
                        Print($"[BE MOVE] {Time[0]:yyyy-MM-dd HH:mm:ss} pos={Position.MarketPosition} entry={entryPrice} newStop={newStop} upTicks={upTicks} (trigger={BE_TriggerTicks})");
                    }

                    SetStopLoss(lastEntryTag, CalculationMode.Price, newStop, false);
                    lastBEBar = CurrentBar;
                    lastBEPrice = newStop;
                }
                catch (Exception ex)
                {
                    Print("[WARN] BE adjustment failed: " + ex.Message);
                }
            }
        }

        private double GetDailyKillLimitUsd()
        {
            if (MaxDailyLossPerContractUSD <= 0)
                return 0;
            
            var eff = GetEffectiveContractsToday();
            return Math.Abs(MaxDailyLossPerContractUSD) * eff;
        }

        private double GetDailyProfitLimitUsd()
        {
            if (MaxDailyProfitPerContractUSD <= 0)
                return 0;

            var eff = GetEffectiveContractsToday();

            return Math.Abs(MaxDailyProfitPerContractUSD) * eff;
        }

        private double GetRealizedToday()
        {
            var cumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            return cumProfit - cumAtSessionOpen;
        }

        private double GetTotalTodayPnlIncludingOpen()
        {
            var realizedToday = GetRealizedToday();
            var unrealized = (Position.MarketPosition == MarketPosition.Flat)
                ? 0.0
                : Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency);

            return realizedToday + unrealized;
        }

        private bool EnforceDailyKill()
        {
            var dailyKill = GetDailyKillLimitUsd();
            if (dailyKill <= 0)
                return false;

            var totalToday = GetTotalTodayPnlIncludingOpen();

            if (DayNotLocked() && totalToday <= -dailyKill)
            {
                dayLocked = DayLocked.MaxLossReached;

                if (DebugMode)
                {
                    Print($"[DAILY KILL] {Time[0]:yyyy-MM-dd HH:mm:ss} totalToday={totalToday:C2} <= -{dailyKill:C2} -> LOCK DAY{(Position.MarketPosition != MarketPosition.Flat ? " + FLATTEN" : "")}");
                }

                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    CancelWorkingOrders();
                    ForceFlatten("DAILY_KILL");
                }

                return true;
            }

            return dayLocked == DayLocked.MaxLossReached;
        }

        private bool EnforceDailyProfitLock()
        {
            var limit = GetDailyProfitLimitUsd();
            if (limit <= 0)
                return false;

            var realizedToday = GetRealizedToday();

            if (DayNotLocked() && realizedToday >= limit)
            {
                dayLocked = DayLocked.DailyProfitReached;

                if (DebugMode)
                {
                    Print(
                        $"[DAY LOCK - PROFIT] {Time[0]:yyyy-MM-dd HH:mm:ss} realizedToday={realizedToday:C2} >= {limit:C2} -> LOCK DAY{(Position.MarketPosition != MarketPosition.Flat ? " + FLATTEN" : "")}");
                }

                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    CancelWorkingOrders();
                    ForceFlatten("DAILY_PROFIT_LOCK");
                }

                return true;
            }

            return dayLocked == DayLocked.DailyProfitReached;
        }

        private bool DayNotLocked()
        {
            return dayLocked == DayLocked.NoLock;
        }

        private int GetContractsForDay(DayOfWeek d)
        {
            var baseQty = Math.Max(1, Contracts);
            return baseQty;
        }

        private int GetEntryQty()
        {
            var d = Time[0].DayOfWeek;
            return GetContractsForDay(d);
        }

        private int GetEffectiveContractsToday()
        {
            return GetContractsForDay(Time[0].DayOfWeek);
        }

        private void TryHardStopByTicks()
        {
            if (hardStopTriggered)
                return;

            if (EmergencyStopTicks < 1)
                return;

            if (entryPriceHard <= 0)
                return;

            if (entryBarIdx >= 0 && CurrentBar <= entryBarIdx)
                return;

            double adverseTicks;

            if (Position.MarketPosition == MarketPosition.Long)
                adverseTicks = (entryPriceHard - Low[0]) / TickSize;
            else
                adverseTicks = (High[0] - entryPriceHard) / TickSize;

            if (adverseTicks < 0)
                adverseTicks = 0;

            if (adverseTicks >= EmergencyStopTicks)
            {
                if (DebugMode)
                {
                    Print(string.Format(
                        "[HARD STOP] {0:yyyy-MM-dd HH:mm:ss} pos={1} entry={2:F2} adverseTicks={3:F1} >= {4} -> FLATTEN",
                        Time[0], Position.MarketPosition, entryPriceHard, adverseTicks, EmergencyStopTicks));
                }

                CancelWorkingOrders();
                ForceFlatten("HARD_STOP_TICKS");
            }
        }

        private void ForceFlatten(string reason)
        {
            if (hardStopTriggered)
                return;

            hardStopTriggered = true;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (!string.IsNullOrEmpty(lastEntryTag))
                    ExitLong(reason, lastEntryTag);
                else
                    ExitLong(reason);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (!string.IsNullOrEmpty(lastEntryTag))
                    ExitShort(reason, lastEntryTag);
                else
                    ExitShort(reason);
            }

            lastEntryTag = string.Empty;
            lastBEBar = -1;
            lastBEPrice = 0.0;
            entryBarIdx = -1;

            entryPriceHard = 0.0;
            entryFillTime = DateTime.MinValue;
            protectiveSeenSinceEntry = false;
        }

        private void CancelWorkingOrders()
        {
            try
            {
                if (Account == null)
                    return;

                foreach (Order o in Account.Orders)
                {
                    if (o == null)
                        continue;

                    if (o.Instrument == null || Instrument == null)
                        continue;

                    if (o.Instrument.FullName != Instrument.FullName)
                        continue;

                    if (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted || o.OrderState == OrderState.PartFilled)
                        Account.Cancel(new[] { o });
                }
            }
            catch (Exception ex)
            {
                if (DebugMode)
                    Print("[WARN] CancelWorkingOrders failed: " + ex.Message);
            }
        }

        private double NormalizeBuyStopPrice(double desiredPrice)
        {
            var ask = GetCurrentAsk();
            if (ask <= 0 || double.IsNaN(ask) || double.IsInfinity(ask))
                return desiredPrice;

            var minPrice = Instrument.MasterInstrument.RoundToTickSize(ask + MinStopOffsetTicks * TickSize);
            return Math.Max(desiredPrice, minPrice);
        }

        private double NormalizeSellStopPrice(double desiredPrice)
        {
            var bid = GetCurrentBid();
            if (bid <= 0 || double.IsNaN(bid) || double.IsInfinity(bid))
                return desiredPrice;

            var maxPrice = Instrument.MasterInstrument.RoundToTickSize(bid - MinStopOffsetTicks * TickSize);
            return Math.Min(desiredPrice, maxPrice);
        }
    }
}
