# Ninjex Overnight Edge Portfolio — PMH regime baseline

Strategy version: `1.5.0-pmh-regime-baseline`.

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

### Prospective drawdown guard

Before every order submission, after the actual initial stop distance is known:

`prospective DD = current peak-to-realized strategy DD + full initial stop risk`

The trade is blocked when prospective DD would exceed
`Maximum Prospective Drawdown Dollars`. Baseline is **$1,650 gross**, leaving
a $150 cushion below the research hard target of $1,800 for commissions,
slippage and live differences.

The equity watermark ratchets on actual open-equity ticks. It is persisted
across days, NinjaTrader/VPS restarts and ES contract changes. State is keyed
separately for:

- live accounts;
- clean Playback research;
- verified-live forensic reruns.

A Playback rewind to an earlier timestamp automatically resets that Playback
risk path. Existing but unreadable/corrupt risk state fails closed.

With a one-contract ES minimum, the prospective guard is a capital-preservation
lock: if remaining risk budget is smaller than the minimum allowed trade risk,
new entries remain blocked rather than risking the account. A genuinely new or
reset account must begin with a clean risk-state file/account identity.

Research telemetry writes
`overnight_edge_portfolio_risk_<instrument>_<RunId>.csv`, including risk-pass,
risk-block, entry/exit, new peak and new maximum intratrade DD observations.

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
- portfolio-risk / intratrade-DD audit.

The perturbation grid intentionally excludes `PMH-CONT-L` because its real
bracket is structure-derived rather than fixed 20/40.
