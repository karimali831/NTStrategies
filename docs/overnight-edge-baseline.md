# Ninjex Overnight Edge Portfolio — PMH regime baseline

Strategy version: `1.5.1-pmh-regime-baseline`.

The baseline is the integrated portfolio validated after the ES 12-26 PMH
deterioration review. Legacy failed-PMH V1/V2 shadow engines and the temporary
Playback-only regime-evaluation toggle have been removed.

## Baseline signals

Priority on the same completed one-minute bar:

1. `PDC-RECLAIM-L`
2. `PMH-REJECT-S` — strict PMH rejection
3. `PMH-CONT-L` — failed-PMH continuation
4. `RTHOPEN-BREAK-S`
5. `PML-BREAK-S`

PDC, RTH-open and PML retain their prior validated rules.

### Strict PMH rejection

The PMH short requires the original sweep/rejection, 0–120 minutes from the
09:30 ET open and completed 5-minute ATR >= 30 ticks, plus:

- signal close at/below completed EMA(9) by default;
- completed 5-minute ATR < 45 ticks by default.

It uses the standard 20-tick stop / 40-tick target.

### PMH continuation

A bullish PMH parent must show completed EMA(9) > EMA(21), nonnegative fast
EMA slope and parent close >= EMA(9). It then requires:

- two completed one-minute closes >= PMH + 2 ticks within 10 minutes;
- a later retest within 6 ticks of PMH, no wick >6 ticks below PMH, close >= PMH;
- a separate bullish follow-through close through the retest high within
  7 minutes, still above PMH and completed EMA(9).

Entry is the first causal tick after confirmation. Stop is below the retest low
plus a 2-tick buffer, minimum 8 ticks, maximum 30 ticks. Target is 2R.

## Portfolio controls

- 1 ES contract
- standard stop / target: 20 / 40 ticks
- max hold: 60 minutes
- daily caps: 3 trades / 2 winners / 2 losses
- flatten: 16:00 ET

### Drawdown handling

The strategy itself does **not** block entries based on a portfolio trailing-
drawdown threshold. This restores the validated PMH-regime baseline that
produced approximately **$25,600 gross across 256 trades** in the accepted
five-contract replay.

That replay's stitched peak-to-trough **intratrade** drawdown was approximately
**$1,925 gross**. Closed-trade drawdown was approximately **$1,575**.

Account-level scaling near a prop-firm drawdown boundary is intentionally an
operator decision (for example, switching from ES to MES). The strategy remains
configured for one ES contract and does not automatically switch instruments
or size down.

When `Enable Research Telemetry = True`, the strategy passively records
intratrade portfolio equity/drawdown to
`overnight_edge_portfolio_drawdown_<instrument>_<RunId>.csv`. This recorder
is observational only: it has no persistence and cannot gate, size, submit,
cancel or modify an order.

## Three base templates

Templates live in
`src/Strategies/NinjexOvernightEdgePortfolio/Templates`.

- **Live.xml** — live baseline; research collection off; verified-live mirroring off;
  restart recovery on.
- **Market Replay - Research.xml** — clean Market Replay/backtesting baseline;
  telemetry + session export on; verified-live mirroring off.
- **Market Replay - Live Rerun.xml** — forensic rerun of what occurred live;
  research collection off; verified-live mirroring on; otherwise trading rules
  match Live.xml.

`Mirror Verified Live Executions` must remain false for clean research and
live trading. It is reserved for the forensic rerun template.

## Research outputs retained

- candidate/trade telemetry;
- 1-minute/session/5-minute context export;
- generic independent candidate shadow execution;
- optional perturbation scenarios for standard fixed-bracket models;
- PMH regime lifecycle events;
- passive portfolio intratrade-DD audit.

The perturbation grid intentionally excludes `PMH-CONT-L` because its real
bracket is structure-derived rather than fixed 20/40.
