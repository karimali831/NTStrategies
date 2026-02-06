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

    public partial class MicroPBV3 : Strategy
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

            var sigClosed = SigClosed();

            // Safety: ensure indicator has that bar
            if (CurrentBar < sigClosed)
                return;

            // Disable BE in strong momentum
            if (DisableBeAboveAdx > 0 && adx[sigClosed] >= DisableBeAboveAdx)
                return;

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
            if (Position.MarketPosition == MarketPosition.Long)
                upTicks = (int)Math.Floor((Close[0] - entryPrice) / TickSize);
            else
                upTicks = (int)Math.Floor((entryPrice - Close[0]) / TickSize);

            if (upTicks < BE_TriggerTicks)
                return;

            var newStop = Position.MarketPosition == MarketPosition.Long
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

            var sig = SigSignal();
            var eff = GetEffectiveContractsToday(sig);

            return Math.Abs(MaxDailyLossPerContractUSD) * eff;
        }

        private double GetDailyProfitLimitUsd()
        {
            if (MaxDailyProfitPerContractUSD <= 0)
                return 0;

            var sig = SigSignal();
            var eff = GetEffectiveContractsToday(sig);

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

        private double GetConsistencyPct()
        {
            switch (ConsistencyRule)
            {
                case ConsistencyRuleMode.ThirtyPercent:
                    return 0.30;
                case ConsistencyRuleMode.FiftyPercent:
                    return 0.50;
                default:
                    return 0.0;
            }
        }

        private double GetProfitBeforeToday()
        {
            if (!strategyStartSet) return 0.0;
            return (cumAtSessionOpen - cumAtStrategyStart);
        }

        private bool EnforceConsistencyRule()
        {
            var pct = GetConsistencyPct();
            if (pct <= 0)
                return false;

            var profitBeforeToday = GetProfitBeforeToday();
            if (profitBeforeToday <= 0)
                return false;

            var realizedToday = GetRealizedToday();
            if (realizedToday <= 0)
                return false;

            var maxToday = (pct / (1.0 - pct)) * profitBeforeToday;

            if (DayNotLocked() && realizedToday > maxToday)
            {
                dayLocked = DayLocked.ConsistencyRule;

                if (DebugMode)
                {
                    Print(string.Format(
                        "[CONSISTENCY LOCK] {0:yyyy-MM-dd HH:mm:ss} realizedToday={1:C2} > maxAllowed={2:C2} (mode={3}, pct={4:P0}, profitBeforeToday={5:C2}) -> LOCK DAY{6}",
                        Time[0], realizedToday, maxToday, ConsistencyRule, pct, profitBeforeToday,
                        Position.MarketPosition != MarketPosition.Flat ? " + FLATTEN" : ""));
                }

                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    CancelWorkingOrders();
                    ForceFlatten("CONSISTENCY_LOCK");
                }

                return true;
            }

            return dayLocked == DayLocked.ConsistencyRule;
        }

        private bool DayNotLocked()
        {
            return dayLocked == DayLocked.NoLock;
        }

        private int GetContractsForDay(DayOfWeek d)
        {
            var baseQty = Math.Max(1, Contracts);

            if (!DynamicContractSizing)
                return baseQty;

            if (d == DayOfWeek.Monday || d == DayOfWeek.Friday)
                return Math.Max(1, baseQty - Math.Max(0, dynamicReduceMonFriBy));

            return baseQty;
        }

        private int GetEntryQty(int sig)
        {
            var d = Time[sig].DayOfWeek;
            return GetContractsForDay(d);
        }

        private int GetEffectiveContractsToday(int sig)
        {
            return GetContractsForDay(Time[sig].DayOfWeek);
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

        private void TryProtectiveWatchdog()
        {
            if (ProtectiveWatchdogSeconds < 1)
                return;

            if (State != State.Realtime || IsInStrategyAnalyzer)
                return;

            if (entryFillTime == DateTime.MinValue)
                return;

            var secsSinceEntry = (Time[0] - entryFillTime).TotalSeconds;
            if (secsSinceEntry < ProtectiveWatchdogSeconds)
                return;

            if (!protectiveSeenSinceEntry)
            {
                if (DebugMode)
                {
                    Print(string.Format(
                        "[WATCHDOG] {0:yyyy-MM-dd HH:mm:ss} No protective stop/target seen within {1}s after entry -> FLATTEN",
                        Time[0], ProtectiveWatchdogSeconds));
                }

                CancelWorkingOrders();
                ForceFlatten("WATCHDOG_NO_PROTECTIVE");
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

        private bool TrendConfirm(int bars, bool longSide)
        {
            return TrendConfirm(bars, longSide, 0, out _, out _);
        }

        private bool TrendConfirm(int bars, bool longSide, int barsAgo)
        {
            return TrendConfirm(bars, longSide, barsAgo, out _, out _);
        }

        private bool TrendConfirm(int bars, bool longSide, int barsAgo, out string failReason, out int failIndex)
        {
            failReason = "none";
            failIndex = -1;

            if (CurrentBar < barsAgo + 1)
            {
                failReason = "insufficient-bars";
                return false;
            }

            var refBar = Math.Max(0, barsAgo);

            if (longSide)
            {
                if (emaSlow[refBar] < emaFast[refBar])
                {
                    if (!(Close[refBar] > emaFast[refBar] && Close[refBar] > emaSlow[refBar]))
                    {
                        failReason = "close-not-above-both-emas";
                        return false;
                    }
                }

                if (!(Close[refBar] >= Open[refBar]))
                {
                    failReason = "not-bullish-candle";
                    return false;
                }

                var bodyTicks = Math.Abs(Close[refBar] - Open[refBar]) / TickSize;
                
                if (StrongBodyTicks > 0 && bodyTicks < StrongBodyTicks)
                {
                    failReason = "body-too-small";
                    return false;
                }
            }
            else
            {
                if (emaSlow[refBar] > emaFast[refBar])
                {
                    if (!(Close[refBar] < emaFast[refBar] && Close[refBar] < emaSlow[refBar]))
                    {
                        failReason = "close-not-below-both-emas";
                        return false;
                    }
                }

                if (!(Close[refBar] <= Open[refBar]))
                {
                    failReason = "not-bearish-candle";
                    return false;
                }

                var bodyTicks = Math.Abs(Close[refBar] - Open[refBar]) / TickSize;
                
                if (StrongBodyTicks > 0 && bodyTicks < StrongBodyTicks)
                {
                    failReason = "body-too-small";
                    return false;
                }
            }

            return true;
        }
    }
}
