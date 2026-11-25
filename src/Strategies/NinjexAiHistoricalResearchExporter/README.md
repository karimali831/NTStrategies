# Ninjex AI historical research exporter

A separate, disabled-by-default NinjaScript strategy for offline OHLCV
research; no HTTP, order submission or machine-learning inference.

Compile NTStrategies in NinjaTrader Windows; open a 5-minute dated ES
chart using CME US Index Futures ETH and set NT global timezone to ET.
Load the appropriate historical contract data (including 1-minute).
Add the Ninjex AI Historical Research Exporter strategy, set Enable
export to True, and set the inclusive yyyy-MM-dd ET date range.
The exporter operates in State.Historical, not Playback realtime.

When the historical calculation finishes, disable/remove the strategy
to flush CSV and manifest. Output goes to the Documents/Ninjex/AiMarket/
Research directory in a new unique folder for each run.

Files:
- bars_1m.csv
- bars_5m.csv
- bars_15m.csv
- bars_60m.csv
- quality_issues.csv
- manifest.json

Each row preserves dated contract, timeframe, UTC close, ET close,
offset minutes, raw OHLCV and quality flags. Data is append-only and
deduplicated only by later offline reconciliation, not overwritten.
Review all source gaps, rollover windows and DST offset warnings.

For 2023-2026, process each dated quarterly ES contract separately.
Split into monthly chunks if loading a quarter strains NinjaTrader.
Use the imported historical data, not a multi-year native Market Replay
download. This exporter does NOT include ticks/quotes, stop-vs-target
collision outcomes, simulated orders, costs, or event labels.
Do not train or score strategies until dataset audits and outcome
labelling are complete.

Portable CI tests cover compile, UTC, fall-back DST, invalid candles
and manifest. Windows NinjaTrader compilation and actual candle-value
comparisons remain mandatory.
