# Integrated PMH regime-split portfolio evaluation

This mode is intentionally **Playback-only** and defaults OFF. It is the first
test that lets the revised PMH family participate in the same real strategy
scheduler, one-position rule and 3/2/2 daily caps as PDC, RTH-open and PML.

It is not a live-production recommendation.

## Portfolio definition

PDC, RTH-open and PML are unchanged.

When `Enable PMH Regime Split Evaluation = True`, the unrestricted Run 8
PMH short is replaced by two mutually exclusive market interpretations.

### 1. Strict bearish PMH rejection

The original PMH sweep/rejection must still satisfy the Run 8 conditions:

- one-minute high > PMH and close < PMH;
- 0–120 minutes from 09:30;
- completed five-minute ATR >= 30 ticks.

Then the integrated evaluation additionally requires:

- signal close distance to completed EMA(9) <= **0 ticks** by default
  (close at/below EMA(9));
- completed five-minute ATR < **45 ticks** by default.

The order remains the ordinary `PMH-REJECT-S`, using the existing 20-tick
stop / 40-tick target.

### 2. Failed-PMH bullish continuation

A qualified original PMH event in bullish context starts a causal observation:

1. parent completed EMA(9) > EMA(21), nonnegative fast-EMA slope, and parent
   close >= EMA(9);
2. within 10 minutes, two consecutive completed one-minute closes >= PMH + 2t;
3. within 10 minutes, a later one-minute bar retests PMH within 6t, never
   breaches more than 6t below, and closes >= PMH;
4. within 7 minutes, a separate bullish one-minute bar closes through the
   retest high, remains >= PMH + 2t and remains above completed EMA(9);
5. the real strategy then competes for the first causal tick using normal
   position state, daily counters and signal priority.

The entry signal is `PMH-CONT-L`. Its bracket is calculated causally from the
first observed entry tick:

- stop distance = ceil(entry tick - retest low in ticks + 2t buffer),
  minimum 8 ticks;
- if required stop > 30 ticks, skip the trade;
- target = 2 × actual stop ticks.

Managed NinjaTrader stop/target orders use those tick distances so they are
fill-relative.

## Signal priority

If multiple executable signals occur on the same completed one-minute bar:

1. PDC reclaim
2. strict PMH rejection short
3. PMH continuation long
4. RTH-open breakdown
5. PML breakdown

Only one pending/active position is permitted. The existing limits remain:

- max 3 entries/day;
- max 2 winners/day;
- max 2 losers/day.

A continuation confirmation that occurs while another position/order is active
or after a daily cap is reached is missed, rather than being shifted to a later
bar.

## Hard safety boundary

When this evaluation switch is enabled, `CanTakeNewTrade` fails closed unless:

- strategy state is Realtime;
- the account/connection is detected as NinjaTrader **Playback**;
- `Mirror Verified Live Executions = False`.

Therefore enabling the evaluation on a normal broker/live connection prevents
all new strategy entries. This is deliberate.

The normal Run 8 path is unchanged when the switch is OFF.

## Exact intratrade drawdown audit

Every delivered one-tick BIP update recalculates:

`strategy equity = cumulative realized gross + active trade unrealized gross`

The evaluation maintains the running equity peak and maximum peak-to-current
drawdown. It writes only entry/exit, new-peak and new-maximum-DD events, avoiding
a multi-million-row tick CSV while preserving the exact observed maximum.

Output:

- `overnight_edge_pmh_regime_integrated_events_*.csv`
- `overnight_edge_pmh_regime_integrated_equity_*.csv`
- ordinary `overnight_edge_research_*.csv`, whose PortfolioMode becomes
  `PmhRegimeIntegratedEvaluation`.

The equity metric is **gross strategy equity**. Commissions and real slippage
must be applied separately.

## Required settings for evaluation

Primary input:

- ES 5 Minute
- CME US Index Futures ETH
- ET timestamps
- historical-tick Playback

Strategy:

- Enable PMH Regime Split Evaluation = **True**
- PMH Short Maximum ATR5 Ticks = **45**
- PMH Short Maximum Fast EMA Distance Ticks = **0**
- Enable Research Telemetry = **True**
- Enable Research Session Export = **True**
- Mirror Verified Live Executions = **False**
- Max Trades / Winners / Losses = **3 / 2 / 2**
- Stop / Target = **20 / 40** for existing models
- V2 retest settings = the current defaults
- V2 Maximum Shadow Stop Ticks = **30** (also used as integrated continuation
  structure-stop maximum)
- V2 Retest Structure Stop Buffer = **2**

V1/V2 independent shadow switches may be OFF for the integrated run. They are
not required for the actual integrated orders.

## Smoke test before the long replay

Run ES 12-26, 2–4 November 2026.

Verify:

1. log prints `PMH REGIME EVALUATION SAFETY Allowed=True`;
2. no unrestricted PMH short occurs solely because the original PMH rule fired;
3. PMH shorts only submit when close <= EMA(9) and ATR <45;
4. any `PMH-CONT-L` uses a structural stop <=30t and 2R target;
5. entry/exit rows have PortfolioMode `PmhRegimeIntegratedEvaluation`;
6. integrated events/equity files exist;
7. daily 3/2/2 limits include PMH-CONT-L exactly like other trades;
8. no additional trade is delayed to a later bar after an original position
   blocks its confirmation;
9. final log prints the maximum intratrade drawdown.

## Five-contract evaluation

Use the same accepted chronological contract windows used for Run 8. Do not
double-count rollover overlap. After each contract, keep that exact RunId.

Run:

```powershell
python tools/analyze_overnight_edge_pmh_regime_integrated.py `
  --input "$env:USERPROFILE\Documents\NinjaTrader 8\NinjexResearch\OvernightEdgePortfolio" `
  --output "$env:USERPROFILE\Desktop\PMH_Regime_Integrated_Summary.csv" `
  --run-id "ES_12_25_RUN_ID" `
  --run-id "ES_03_26_RUN_ID" `
  --run-id "ES_06_26_RUN_ID" `
  --run-id "ES_09_26_RUN_ID" `
  --run-id "ES_12_26_RUN_ID"
```

The report uses **actual strategy TradeExit rows**, not V1/V2 shadow outcomes,
and reports the maximum recorded intratrade drawdown per contract.

## Decision criteria

Do not promote the revised PMH family merely because total gross improves.

Minimum evaluation targets:

- ES 12-26 clearly positive;
- worst recorded intratrade drawdown <= **$1,800 gross** before considering
  commissions/slippage;
- no catastrophic contract;
- whole-portfolio historical result competitive with Run 8;
- useful recent frequency (roughly 3+ portfolio trades/week);
- no further parameter tuning before a meaningful forward/held-out period.

If this integrated test fails, the fallback is to retire PMH from executable
orders and retain PDC + PML + RTH rather than continue fitting the PMH family.
