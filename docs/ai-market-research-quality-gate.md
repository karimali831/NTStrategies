# Ninjex AI — historical research quality gate

Do not begin ML training merely because observations arrived in SQL.
The live publisher stores a deterministic regime classification and may
skip bars without a valid multi-timeframe snapshot.

## How to use

Run the separate Ninjex AI Historical Research Exporter on a dated ES/NQ
historical chart (5-minute primary; CME US Index Futures ETH; NT time zone ET).
It exports unique OHLCV rows for 1m, 5m, 15m and 60m. Disable the
exporter after historical processing to flush all CSVs and manifest.json.

Install Python time-zone data on Windows:

    python -m pip install tzdata

Run the quality gate from the NTStrategies repository root.
Replace YOUR_RUN_FOLDER with the unique output directory printed in
NinjaTrader's [NinjexAI-EXPORT] Output message.

    python tools/ai_market_research_audit.py --contract "ES 12-26" --folder "C:\Users\karim\Documents\Ninjex\AiMarket\Research\YOUR_RUN_FOLDER" --from-et "2026-09-27T18:05:00" --through-et "2026-09-30T23:55:00" --output "C:\Temp\ninjex-quality-report.json"

The Documents location can differ on OneDrive machines; use the actual
path reported in the NinjaTrader log.

The audit checks each timeframe for expected ETH candle closing times,
duplicate/out-of-order UTC, correct ET offset, invalid OHLCV, ES/NQ tick
size, missing data and off-session bars. It also validates the exporter
manifest and reconstructs 5m OHLCV from its five 1m constituents.

A zero-error result is PASS_WITH_MANUAL_CALENDAR_REVIEW, NOT a blanket
certification. Verify holiday and early-close schedules, official
rollovers, any source data gaps and several candles against original NT
chart/tick data before using the dataset to train ML.

Research requirements beyond this tool: outcomes labelled strictly
after each decision time, robust walk-forward/hold-out testing, and
high-quality tick-level trade paths to resolve intrabar stop/target order.
This tool neither trains models nor submits orders.
