# Ninjex AI v11 — first REAL historical Last-tick research input

## Why this exists

The v10 21-EMA H1/H2 mechanical detector found 1,074 first attempts
(H1/L1) and only 42 second-entry H2/L2 candidates across historical
TRAIN. Risk and selection left 180 filled H1 and **10 filled H2**.
H1: **-$4,575 net**, 180 fills, PF 0.7359, closed drawdown $5,957.50.
H2: **+$450**, 10 fills, PF 1.6143, closed drawdown $352.50;
ONLY 2 early-period H2 fills made +$440 and 8 later-period H2
fills made +$10. H2 LONG was 5 winners +$1,125, H2 SHORT
five trades -$675. This is grossly insufficient for generalization
and selection after observing a positive subgroup would be biased.

The ranker **correctly did NOT train**: required >=50 early and >=30
later training H2 fills; found 2 and 8. No profitable AI exists.

Dominant setup-rejection reason: 841 / 1,116 potential signals
breached the $250 one-ES nominal stop-risk cap. It is *not safe* to
increase the limit or silently switch to MES just to get a prettier
backtest. The 5-minute signal-bar size and EMA are fundamentally
different from a true count-based tick chart.

The historical NinjaTrader AI exporter produces 1/5/15/60-minute
OHLCV; the old tick-data audit inventories native .ncd files and
optional *exported* Last tick text but does not decode .ncd binary.

## New v11 tool: minimal bounded tick-pipeline pilot

tools/ai_market_tick_pilot_v11.py is a READ-ONLY, LOCAL, single-source
native Last tick TEXT importer using the timestamp and numeric format
that src/Tools/Audit-NinjaTraderTickData/Audit-NinjaTraderTickData.ps1
already audits. It does **not** read .ncd binary or NinjaTrader's
live account, send orders or call the OpenAI API.

To pilot:
1. In NinjaTrader Historical Data Export, choose one dated ES
   contract (for example ES 06-24), a date/week within original
   TRAIN (before Sep 1 2025 ET), Interval **Tick**, price type
   **Last**, and export to native text. Do not upload full 3-year
   ticks to a chat.
2. Choose a filename and note the EXACT absolute local path.
3. Update the --input path below to that actual file and run from
   C:\Projects\NTStrategies:

    python .\tools\ai_market_tick_pilot_v11.py --input "C:\PATH\TO\YOUR\EXPORTED-LAST-TICKS.txt" --contract "ES 06-24" --ticks-per-bar 2000 --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\TickPilotV11"

4. If an export has millions of rows, optionally first use
   --limit-rows 200000. The output will explicitly say that the
   file was only partially processed. Rerun without this limit for
   a whole-source pilot when feasible.

Files in the output:
- ES_06-24-2000tick-audit-v11.json
- ES_06-24-2000tick-train-v11.csv

**Send only the small JSON audit first**, plus the name and extent
of the sample export. Keep large CSV/tick sources local.

### Integrity and feature rules

- Accept exactly NinjaTrader native 3-field or 5-field Last text
  rows: UTC 'yyyyMMdd HHmmss [fffffff]';last;size;[two extras].
  Parse seven fractional timestamp digits preserving the final
  100ns digit for ordering (Python datetime stores microseconds).
- Validate each ES price is positive and precisely aligned to a
  $0.25 tick; Last size is positive integer; timestamps must not
  reverse. Identical timestamps are legal.
- Extra fields on 5-column exports are *UNCLASSIFIED*. Never claim
  they are bid/ask, or that they establish aggressive volume,
  unless the exact provider schema is verified.
- Stream one source file at a time; no multi-quarter / rollover
  joins, no backfilled missing data, no data repair.
- Build exact tick-count bars by counting raw LAST records, NOT
  by summing their volume. Default 2,000 records/bar, configurable.
- Discard partial bar / EMA history at source gaps >20 minutes.
  Output only complete tick-count bars.
- Output OHLCV, first-to-last duration, 21-EMA and slope5 computed
  from *completed tick bars*, ATR14 from tick-bar true range,
  bar body/range and UTC/New York end timestamps. Features are
  pre-trade as-of, NO future outcome labels.
- Original TRAIN is before Sep 1 2025 ET. At the first row on
  or after this boundary, stop BEFORE decoding its price/size or
  other extras. For chronology we inspect timestamp only. If the
  source file is artificially unsorted, the script fails rather
  than trying to repair.
- Hash accepted input record bytes only (prefix hash); explicit
  warning if source extends beyond cutoff or row-limit.
- 2,000 "Last updates" is not guaranteed to mean 2,000 exchange
  transactions, and is not asserted identical to a particular
  NinjaTrader Chart Bars Type.Tick configuration without
  running a same-timestamp/chart comparator.

## What v11 does NOT yet do

v11 is the **first authentic tick-source ingestion stage**, not
an AI-discovered strategy, full historical data pipeline, or a
validated trade-entry engine. It has NOT run on the user's Windows
historical source yet. Its synthetic CI tests validate parser,
bar construction, immutability and training boundary only.

Next, after a valid pilot:
- Verify one representative exported tick source against the
  original NinjaTrader 2,000-tick chart: bar close timestamp,
  OHLC, bar count, session breaks, volume, price offsets.
- Stream full TRAIN dated-contract source in bounded chronological
  chunks, with rollover and coverage manifests, duplicate rejection,
  evidence of missing source days and deterministic output.
- Implement a walk-forward **AI-driven event/representation
  discovery service**, not an endless manual strategy catalogue.
  Build as-of features from tick flow, tick-bar patterns, price
  efficiency, volatility expansion, duration and volume. Distinguish
  trade-only Last microstructure proxies from TRUE bid/ask quote data.
- Define labels/exit policies before modelling, make outcomes
  tick-sequential where possible, and use a nested chronological
  walk-forward for selection with multiple-testing adjustment.
- Only pre-registered robust ideas get ONE final evaluation on
  untouched June15 2026+ holdout, then NinjaTrader sim/shadow trading
  and an intraday open-equity drawdown audit.

The desired >=$10k annual net with <=$2k drawdown is an aspiration,
not proof that any historical dataset contains a viable edge.
