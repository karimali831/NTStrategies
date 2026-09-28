from pathlib import Path

path = Path('src/Strategies/NinjexOvernightEdgePortfolio/Core.cs')
text = path.read_text(encoding='utf-8-sig')

def replace_once(old, new, label):
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{label}: expected exactly 1 match, found {count}')
    text = text.replace(old, new, 1)

replace_once(
    'private const string StrategyVersion = "1.2.3-verified-rth";',
    'private const string StrategyVersion = "1.2.4-research-telemetry";',
    'version')

replace_once(
    'public class NinjexOvernightEdgePortfolio : Strategy',
    'public partial class NinjexOvernightEdgePortfolio : Strategy',
    'partial class')

replace_once(
    '''                EnableDiagnostics = true;\n\n                // Disabled by default. Verified overrides are intended only''',
    '''                EnableDiagnostics = true;\n                EnableResearchTelemetry = false;\n\n                // Disabled by default. Verified overrides are intended only''',
    'telemetry default')

replace_once(
    '''                emaSlow5m =\n                    EMA(\n                        Closes[ContextSeriesIndex],\n                        EmaSlowPeriod);\n\n                Diagnostic(''',
    '''                emaSlow5m =\n                    EMA(\n                        Closes[ContextSeriesIndex],\n                        EmaSlowPeriod);\n\n                InitializeResearchTelemetry();\n\n                Diagnostic(''',
    'telemetry init')

replace_once(
    '''                    "MaxTrades={7} MaxWinners={8} MaxLosses={9} " +\n                    "VerifiedRthOpenOverrides={10}",''',
    '''                    "MaxTrades={7} MaxWinners={8} MaxLosses={9} " +\n                    "VerifiedRthOpenOverrides={10} " +\n                    "ResearchTelemetry={11} ResearchPath='{12}'",''',
    'ready format')

replace_once(
    '''                    MaxLossesPerDay,\n                    EnableVerifiedRthOpenOverrides);\n            }\n        }''',
    '''                    MaxLossesPerDay,\n                    EnableVerifiedRthOpenOverrides,\n                    EnableResearchTelemetry,\n                    researchTelemetryPath);\n            }\n            else if (State == State.Terminated)\n            {\n                DisposeResearchTelemetry();\n            }\n        }''',
    'ready args terminated')

replace_once(
    '''            //\n            // Cache exactly the completed 5-minute context.\n            // This mirrors the neutral collector: signal rows do not\n            // peek into the still-forming 5-minute candle.\n            //\n            last5mTime =''',
    '''            //\n            // Preserve the previous completed EMA values for observational\n            // research telemetry before the authoritative context is updated.\n            // This does not participate in signal qualification.\n            //\n            CaptureResearchPreviousFiveMinuteContext(\n                last5mEmaFast,\n                last5mEmaSlow);\n\n            //\n            // Cache exactly the completed 5-minute context.\n            // This mirrors the neutral collector: signal rows do not\n            // peek into the still-forming 5-minute candle.\n            //\n            last5mTime =''',
    'previous ema telemetry')

replace_once(
    '''            var currentBarOpen =\n                Opens[SignalSeriesIndex][0];\n\n            var high =''',
    '''            var currentBarOpen =\n                Opens[SignalSeriesIndex][0];\n\n            var signalOpen =\n                Opens[SignalSeriesIndex][1];\n\n            var high =''',
    'signal open')

replace_once(
    '''            if (!CanEvaluateSignal(\n                    signalTime))\n            {\n                return;\n            }\n\n\n            if (!CanTakeNewTrade(\n                    signalTime,\n                    true,\n                    false))\n            {\n                return;\n            }\n\n\n            var minutesFromOpen =\n                MinutesBetween(\n                    MarketOpenTime,\n                    timeValue);\n\n            var overnightWidthTicks =\n                GetOvernightWidthTicks();\n\n\n            if (PortfolioMode''',
    '''            if (!CanEvaluateSignal(\n                    signalTime))\n            {\n                return;\n            }\n\n\n            var minutesFromOpen =\n                MinutesBetween(\n                    MarketOpenTime,\n                    timeValue);\n\n            var overnightWidthTicks =\n                GetOvernightWidthTicks();\n\n\n            // Capture raw four-model candidates before portfolio-state gating so\n            // research can see signals displaced by an active trade or daily cap.\n            // Core execution below remains unchanged and authoritative.\n            if (PortfolioMode\n                != NinjexOvernightEdgePortfolioMode.BaselineAB)\n            {\n                CaptureFourModelResearchCandidates(\n                    signalTime,\n                    signalOpen,\n                    high,\n                    low,\n                    close,\n                    previousClose,\n                    minutesFromOpen,\n                    overnightWidthTicks);\n            }\n\n\n            if (!CanTakeNewTrade(\n                    signalTime,\n                    true,\n                    false))\n            {\n                return;\n            }\n\n\n            if (PortfolioMode''',
    'pre-gate telemetry')

replace_once(
    '''            pendingMinutesFromOpen =\n                minutesFromOpen;\n\n\n            Diagnostic(''',
    '''            pendingMinutesFromOpen =\n                minutesFromOpen;\n\n            AttachPendingResearchSignal(\n                signalTime,\n                entrySignal);\n\n\n            Diagnostic(''',
    'attach pending telemetry')

replace_once(
    '''            grossPnlToday = 0;\n\n\n            ClearPendingEntry(''',
    '''            grossPnlToday = 0;\n\n            ResetResearchTelemetryDailyState();\n\n\n            ClearPendingEntry(''',
    'daily telemetry reset')

replace_once(
    '''            pendingMinutesFromOpen = 0;\n        }''',
    '''            pendingMinutesFromOpen = 0;\n\n            ClearPendingResearchSignal();\n        }''',
    'clear pending telemetry')

replace_once(
    '''            if (isLongEntry\n                || isShortEntry)\n            {\n                entryOrderPending = false;\n\n\n                if (!activeTradeCounted)''',
    '''            if (isLongEntry\n                || isShortEntry)\n            {\n                entryOrderPending = false;\n\n                var isNewResearchTrade =\n                    !activeTradeCounted;\n\n\n                if (!activeTradeCounted)''',
    'new research trade marker')

replace_once(
    '''                activeEntryPriceQuantity +=\n                    price * quantity;\n\n\n                Diagnostic(''',
    '''                activeEntryPriceQuantity +=\n                    price * quantity;\n\n                if (isNewResearchTrade)\n                {\n                    RecordResearchEntryFill(\n                        time,\n                        GetActiveAverageEntryPrice());\n                }\n\n\n                Diagnostic(''',
    'entry research fill')

replace_once(
    '''                FinalizeActiveTrade(\n                    time,\n                    order.Name);''',
    '''                FinalizeActiveTrade(\n                    time,\n                    order.Name,\n                    price);''',
    'finalize call price')

replace_once(
    '''        private void FinalizeActiveTrade(\n            DateTime time,\n            string exitName)''',
    '''        private void FinalizeActiveTrade(\n            DateTime time,\n            string exitName,\n            double exitPrice)''',
    'finalize signature')

replace_once(
    '''            else if (activeTradeGrossPnl < 0)\n                lossesToday++;\n\n\n            Diagnostic(''',
    '''            else if (activeTradeGrossPnl < 0)\n                lossesToday++;\n\n            RecordResearchTradeExit(\n                time,\n                exitName,\n                exitPrice);\n\n\n            Diagnostic(''',
    'trade exit telemetry')

replace_once(
    '''                    if (filled <= 0\n                        && !activeTradeCounted)\n                    {\n                        submittedSignalTime =\n                            Core.Globals.MinDate;''',
    '''                    if (filled <= 0\n                        && !activeTradeCounted)\n                    {\n                        ClearPendingResearchSignal();\n\n                        submittedSignalTime =\n                            Core.Globals.MinDate;''',
    'rejected entry telemetry clear')

path.write_text(text, encoding='utf-8-sig')
print('patched Core.cs successfully')
