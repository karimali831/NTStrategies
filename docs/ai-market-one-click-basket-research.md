# Ninjex AI - bulk ES historical research with NinjaTrader Basket Testing

This is a no-trade, local-data-only batch research workflow.

## New strategy option

The Ninjex AI Historical Research Exporter now offers:
Auto quarterly contract windows (basket) = true.

For each dated instrument in a Strategy Analyzer Basket Test, its C# window
calculator applies the conventional CME roll Monday of the third-Friday
expiration week, with the next contract beginning on the prior Sunday
evening. This choice is a RESEARCH rollover convention, not proof of the
best volume crossover. Preserve Do Not Merge for unadjusted dated prices.

Each instrument has its own raw CSV files and manifest, with its own
quarterly date range derived from the same global export settings.

## Two-contract pilot (one test, no 13 repeated manual runs)

1. Pull this branch and compile NinjaScript on Windows.
2. Control Center > Tools > Instrument Lists: create Ninjex ES Pilot
   containing ES 12-23 and ES 03-24.
3. New > Strategy Analyzer, Backtest: select Ninjex ES Pilot as the
   instrument list (basket), not one individual futures contract.
4. Strategy: Ninjex AI Historical Research Exporter.
5. Enable export = checked.
6. Auto quarterly contract windows (basket) = checked.
7. Recover source 17:00 close = checked.
8. Dataset tag = es-basket-pilot-v1.
9. Exporter global From ET 2023-09-10, Through ET 2024-03-08.
10. Analyzer Start date 2023-09-08, End date 2024-03-11.
11. Trading hours CME US Index Futures ETH; timezone Eastern Time.
    Primary 5-minute Last, Standard (Fastest), Tick Replay off.
12. Click Run ONCE. Look for two BASKET log lines:
    ES 12-23: firstET=2023-09-10 throughET=2023-12-08
    ES 03-24: firstET=2023-12-10 throughET=2024-03-08.
    There should be two separate output folders and two manifests.

If only one contract exported, do not proceed to the full set.
Send the NinjaTrader Output and the two manifests for investigation.

## Run the single batch quality report

Open PowerShell in the NTStrategies repository root, then use:

python .\tools\ai_market_batch_audit.py --root "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research" --tag "es-basket-pilot-v1" --from-et "2023-09-10" --through-et "2024-03-08" --contract "ES 12-23" --contract "ES 03-24" --calendars ".\tools\ai_market_calendars" --allow-sparse-1m --output "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-basket-pilot-audit.json"

The script creates JSON and Markdown reports with a single table of
missing candles, per-contract pass/fail, and review-required exceptions.

Existing exports with other dataset tags are ignored. If two folders
share the same tag and contract, the report flags DUPLICATE_RUNS rather
than arbitrarily picking one.

## Full historical collection

Create one Instrument List containing all 13 dated ES contracts:

ES 12-23, ES 03-24, ES 06-24, ES 09-24, ES 12-24,
ES 03-25, ES 06-25, ES 09-25, ES 12-25,
ES 03-26, ES 06-26, ES 09-26, ES 12-26.

Use the same strategy with overall dates 2023-09-10 through
2026-11-25 and tag es-2023-2026-basket-v1. Analyzer dates
2023-09-08 through 2026-11-30. The exporter picks each dated
quarterly research window automatically.

Start with a two-contract pilot and consider groups of 3-4 for memory
safety. A single huge basket may be much slower or exceed NinjaTrader
memory, even though it is one user action.

Run the same batch audit command without individual --contract options
and with the full-range tag. Only flagged exceptions require inspection.

A reported failure around Christmas, New Year, MLK, Presidents Day,
Good Friday or another official early closure may reflect the lack
of a reviewed special-hours calendar. Add documented exceptions to
the shared calendar directory ONCE and rerun the audit; never patch
source bars just to make the report pass.

The ML raw data remains immutable, sparse-minute gaps are flagged,
and missing/future/reversed candles or unexplained volume fail.
No order execution and no ML training occurs in this collection step.

Documentation:
https://static.ninjatrader.com/support/helpGuides/nt8/basket_test.htm
https://www.cmegroup.com/trading/equity-index/rolldates.html

## ES 12-23 / ES 03-24 pilot: expected holiday closures

The initial basket run produced both correct tagged datasets and the
consolidated JSON and Markdown reports. It also tried eleven out-of-range
instruments and logged STARTUP ERROR: No overlap. New basket runs instead
record SKIPPED_OUTSIDE_RESEARCH_WINDOW without creating extra folders.

The new shared calendar
tools/ai_market_calendars/es_winter_2023_2024.json
covers Christmas Day 2023, New Year's Day 2024, Martin Luther King Jr.
Day 2024 and Presidents' Day 2024, including the source-supported
early market halts at 13:00 ET. Existing --calendars argument loads
all reviewed JSONs without changing the previously exported CSVs.

ES 03-24 still has one independent missing five-minute close:
Friday February 2, 2024 at 16:25 ET. The underlying export also lacks
nine consecutive 1m closes from 16:19 through 16:27 ET; the containing
15m bar reconciles with existing 1m/5m OHLCV. It may be a no-trade
interval or source outage. Strict audit must continue to fail until the
exception is reviewed; do not synthesize the missing bar for execution
analysis or use one-minute feature/label windows overlapping it.

The quality gate now records missingByEasternDate per timeframe, and
the consolidated report shows missing5mByEasternDate for targeted checks.

The pilot's existing tagged CSVs need NO re-export after calendar update:
rerun the batch quality audit using the same tag and output parameters.
Future ML ingestion can mask unresolved windows without discarding
entire quarters of usable historical candles.
