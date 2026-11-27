#!/usr/bin/env python3
"""Ninjex AI v7 — NY cash-session opening-range event hypotheses, TRAIN ONLY.

Not an ML model, trading system or independent validation. The first
completed 5m RTH candle closes 09:35 America/New_York, and the first
6 candles through 10:00 form the 30m opening range. Search 10:05–11:30
for the first closed 5m breakout or failed sweep; at most 1 event/day.
Require complete raw 5m history; use next 1m open for hypothetical
12-tick stop, 24-tick target, 30m expiry, stop-first on ambiguity,
1 adverse tick per side and $5 round-trip. No source 1m gaps filled.
Only TRAIN <2025-09-01 ET; all later raw OHLC excluded BEFORE loading.
"""
import argparse
import datetime as dt
import json
from collections import Counter
from pathlib import Path

import numpy as np
import pandas as pd

if __package__:
    from tools.ai_market_ml_features import NY
    from tools.ai_market_train_baseline import read_verified_csv
    from tools.ai_market_bracket_research import minute_window, simulate, summarize
    from tools.ai_market_frozen_reverse_v6 import read_raw_before
else:
    from ai_market_ml_features import NY
    from ai_market_train_baseline import read_verified_csv
    from ai_market_bracket_research import minute_window, simulate, summarize
    from ai_market_frozen_reverse_v6 import read_raw_before

TICK = 0.25
TRAIN_END = dt.date(2025, 9, 1)
EARLY_END = dt.date(2024, 9, 1)
RAW_END_UTC = dt.datetime(2025, 9, 1, tzinfo=NY).astimezone(dt.timezone.utc)
RANGE_CLOCKS = tuple(range(575, 601, 5))  # 09:35..10:00 inclusive
SIGNAL_CLOCKS = tuple(range(605, 691, 5))  # 10:05..11:30 inclusive
EVENTS = ("BREAK_LONG","BREAK_SHORT","REJECT_LONG","REJECT_SHORT")
SIDES = ("signal","opposite","alwaysLong","alwaysShort")


def utc_close(day, minute):
    return dt.datetime.combine(
        day, dt.time(minute // 60, minute % 60), tzinfo=NY
    ).astimezone(dt.timezone.utc)


def trigger(current, high, low):
    if high <= low:
        raise ValueError("Opening range has nonpositive width")
    if "INVALID" in current.flags:
        return "AMBIGUOUS"
    labels = [
        name for condition, name in (
            (current.close >= high + TICK and current.close > current.open, "BREAK_LONG"),
            (current.close <= low - TICK and current.close < current.open, "BREAK_SHORT"),
            (current.low <= low - TICK and current.close >= low + TICK
             and current.close > current.open, "REJECT_LONG"),
            (current.high >= high + TICK and current.close <= high - TICK
             and current.close < current.open, "REJECT_SHORT")
        ) if condition
    ]
    return labels[0] if len(labels) == 1 else ("AMBIGUOUS" if len(labels) > 1 else None)


def first_event(day, five):
    opens = [five.get(utc_close(day, k)) for k in RANGE_CLOCKS]
    if any(b is None or "INVALID" in b.flags for b in opens):
        return None, "missingOpeningRange5m"
    high = max(b.high for b in opens)
    low = min(b.low for b in opens)
    if high <= low:
        return None, "zeroRange"
    for minute in SIGNAL_CLOCKS:
        bar = five.get(utc_close(day, minute))
        if bar is None or "INVALID" in bar.flags:
            return None, "gapBeforeFirstEvent"
        event = trigger(bar, high, low)
        if event == "AMBIGUOUS":
            return None, "ambiguousFirstEvent"
        if event:
            return (event, bar.end, high, low), None
    return None, "noEvent"


def selected_side(row, policy):
    side = "LONG" if row["event"].endswith("_LONG") else "SHORT"
    if policy == "signal":
        return side
    if policy == "opposite":
        return "SHORT" if side == "LONG" else "LONG"
    if policy == "alwaysLong":
        return "LONG"
    if policy == "alwaysShort":
        return "SHORT"
    raise ValueError("Unknown policy")


def policy_results(rows):
    result = {}
    for policy in SIDES:
        trades = []
        for r in rows:
            side = selected_side(r, policy)
            trades.append({
                "decisionUtc": r["decisionUtc"],
                "month": r["month"],
                "netUsd": r["netLong"] if side == "LONG" else r["netShort"],
                "exitReason": r["reasonLong"] if side == "LONG" else r["reasonShort"],
                "bothTouched1m": r["ambigLong"] if side == "LONG" else r["ambigShort"],
            })
        result[policy] = summarize(trades)
    return result


def paired_day_ci(rows):
    if len(rows) < 2:
        return {"days":len(rows),"meanUsd":None,"ci95":None}
    differences = np.array([
        r["netLong"] - r["netShort"] if r["event"].endswith("_LONG")
        else r["netShort"] - r["netLong"] for r in rows
    ], dtype=float)
    rng=np.random.default_rng(42)
    sample=rng.integers(0,len(differences),(1000,len(differences)))
    interval=np.quantile(differences[sample].mean(axis=1),[.025,.975])
    return {"days":len({r["easternDate"] for r in rows}),
            "meanUsd":round(float(differences.mean()),3),
            "ci95":[round(float(k),3) for k in interval]}


def run(features, audit, output):
    meta=json.loads(features.with_suffix(".manifest.json").read_text("utf-8-sig"))
    basket=json.loads(audit.read_text("utf-8-sig"))
    if basket.get("schema")!="ninjex-basket-audit-v1" or (
        basket.get("datasetTag")!=meta.get("datasetTag")
    ):
        raise ValueError("Historical source tag mismatch")
    all_data=read_verified_csv(features,meta)
    counts={s:int((all_data["researchSplit"]==s).sum())
            for s in ("train","validation","holdout")}
    training=all_data.loc[all_data["researchSplit"]=="train"].copy()
    del all_data
    if training.empty:
        raise ValueError("No train split")
    training["easternDate"]=(pd.to_datetime(training["decisionUtc"],utc=True)
                             .dt.tz_convert(NY).dt.date)
    if (training["easternDate"] >= TRAIN_END).any():
        raise ValueError("TRAIN contains future market dates")
    paths={x["contract"]:Path(x["folders"][0]) for x in basket["contracts"]
           if len(x.get("folders",[]))==1}
    # Reject any ET date represented by more than one dated contract.
    ambiguous_days=set(training.groupby("easternDate")["contract"].nunique()
                       .loc[lambda counts:counts>1].index)
    excluded=Counter()
    records=[]
    for contract,contract_features in training.groupby("contract",sort=True):
        if contract not in paths:
            raise ValueError("Missing unique original export: "+contract)
        folder=paths[contract]
        manifest=json.loads((folder/"manifest.json").read_text("utf-8-sig"))
        if manifest.get("contract")!=contract or manifest.get("datasetTag")!=meta["datasetTag"]:
            raise ValueError("Raw contract provenance mismatch")
        five=read_raw_before(folder,5,contract,RAW_END_UTC)
        one=read_raw_before(folder,1,contract,RAW_END_UTC)
        for day,rows in contract_features.groupby("easternDate",sort=True):
            if day in ambiguous_days:
                excluded["multipleContractDay"]+=1
                continue
            if day.weekday()>=5:
                continue
            candidate,why=first_event(day,five)
            if candidate is None:
                excluded[why]+=1
                continue
            event,t,hi,lo=candidate
            if t+dt.timedelta(minutes=30)>=RAW_END_UTC:
                excluded["crossTrainBoundary"]+=1
                continue
            matching=rows.loc[pd.to_datetime(rows["decisionUtc"],utc=True)==pd.Timestamp(t)]
            if len(matching)!=1:
                excluded["missingOrDuplicateFeature"]+=1
                continue
            feature=matching.iloc[0]
            if pd.to_numeric(feature["fine1mComplete"],errors="coerce")!=1 or (
                    str(feature["qualityFlags"])!="OK"):
                excluded["sourceQuality"]+=1
                continue
            if abs(float(feature["close"])-five[t].close)>1e-7:
                raise ValueError("Feature 5m close conflicts with source")
            future=minute_window(one,t)
            if future is None:
                excluded["missingFutureOneMinute"]+=1
                continue
            long=simulate(future,1,12,24)
            short=simulate(future,-1,12,24)
            records.append({
                "contract":contract, "easternDate":day.isoformat(),
                "decisionUtc":t.isoformat(),"month":day.strftime("%Y-%m"),
                "period":"EARLY_TRAIN" if day<EARLY_END else "LATE_TRAIN",
                "event":event,"orWidthTicks":round((hi-lo)/TICK,2),
                "orHigh":hi,"orLow":lo,
                "netLong":long["netUsd"],"netShort":short["netUsd"],
                "reasonLong":long["exitReason"],"reasonShort":short["exitReason"],
                "ambigLong":long["bothTouched1m"],
                "ambigShort":short["bothTouched1m"],
            })
    records.sort(key=lambda r:r["decisionUtc"])
    if not records:
        raise ValueError("No complete RTH opening range events")
    if len({r["easternDate"] for r in records})!=len(records):
        raise ValueError("More than one market event per ET day")
    overall=policy_results(records)
    per_event={event:policy_results([r for r in records if r["event"]==event])
               for event in EVENTS}
    periods={
        event:{period:policy_results([r for r in records
                    if r["event"]==event and r["period"]==period])
               for period in ("EARLY_TRAIN","LATE_TRAIN")}
        for event in EVENTS
    }
    result={
        "schema":"ninjex-ai-ny-opening-range-v7",
        "status":"TRAIN_ONLY_HYPOTHESIS_NO_VALIDATED_TRADING_EDGE",
        "featureSource":str(features),"auditSource":str(audit),
        "sourceAuditStatus":basket.get("status"),
        "trainFeatureRows":counts["train"],
        "validationFeatureRowsNotScored":counts["validation"],
        "holdoutFeatureRowsNotScored":counts["holdout"],
        "rawAfterSep2025NotLoaded":True,
        "sourceDays":training["easternDate"].nunique(),
        "excludedDays":dict(sorted(excluded.items())),
        "oneEventPerDayCount":len(records),
        "eventCounts":dict(sorted(Counter(r["event"] for r in records).items())),
        "predeclaredPolicy":{
            "openingRange":"Six consecutive completed 5m candles ending 09:35 through 10:00 America/New_York",
            "firstSignal":"first unique candle event, 10:05 through 11:30 Eastern, at most one/day",
            "breakout":"close outside opening range by >=one tick in candle direction",
            "rejection":"sweep opening range by >=one tick then close >=one tick back inside, reverse candle",
            "entry":"next raw 1m candle open",
            "stopTicks":12,"targetTicks":24,"maximumHoldMinutes":30,
            "adverseSlippageTicksPerSide":1,"roundTripCommissionUsd":5,
            "sameMinuteTwoTouches":"STOP FIRST",
            "pairedBaselines":list(SIDES),
        },
        "overall":overall,"byEvent":per_event,"byEventPeriod":periods,
        "byContract":{
            c:policy_results([r for r in records if r["contract"]==c])
            for c in sorted({r["contract"] for r in records})
        },
        "signalMinusOppositeDayBootstrap":paired_day_ci(records),
        "warnings":[
            "No model fit, no stop/target or signal grid search; TRAIN research only.",
            "Researcher has already inspected earlier market periods in v1-v6, so no independent claim follows.",
            "Prior-used validation Sep2025-Jun2026 and final June2026 onward holdout not scored or loaded as raw.",
            "1m OHLC is NOT a tick-accurate executable order simulation or broker PnL.",
            "Fixed 12/24-tick bracket, one-tick adverse slippage per side and $5 round-trip fees are assumed.",
            "Data audit remains provisional and missing minute/candle data are NOT imputed.",
            "Max closed-equity DD is not intraday unrealized prop firm trailing DD.",
            "Do not declare a profitable event/contract/period subgroup to be a discovered edge."
        ],
    }
    output.mkdir(parents=True,exist_ok=True)
    (output/"es-ny-opening-range-v7.json").write_text(json.dumps(result,indent=2),encoding="utf-8")
    pd.DataFrame(records).to_csv(output/"es-ny-opening-range-v7-events.csv",index=False)
    lines=[
        "# Ninjex AI v7 — NY cash opening-range hypotheses (TRAIN only)",
        "",
        "**No validation or final holdout scored. No ML fit or trade orders.**",
        "",
        "One-event-per-day research entries: {:,}.".format(len(records)),
        "Validation excluded: {:,}; holdout excluded: {:,}.".format(
            counts["validation"],counts["holdout"]),
        "",
        "| Direction | Trades | Net USD | Mean USD/entry | PF | Closed DD |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for name,s in overall.items():
        lines.append("| {} | {} | {:.2f} | {:.2f} | {} | {:.2f} |".format(
            name,s["trades"],s["netUsd"],s["meanPerTradeUsd"],
            s["profitFactor"],s["maxClosedEquityDrawdownUsd"]))
    lines+=["","## Frozen structure families","",
            "| Event | N | Signal mean USD | Opposite mean USD |",
            "|---|---:|---:|---:|"]
    for name,policies in per_event.items():
        lines.append("| {} | {} | {:.2f} | {:.2f} |".format(
            name,policies["signal"]["trades"],
            policies["signal"]["meanPerTradeUsd"],
            policies["opposite"]["meanPerTradeUsd"]))
    lines+=["","## Early vs late TRAIN (descriptive only)",""]
    for name,by_period in periods.items():
        lines.append("### "+name)
        for period,policies in by_period.items():
            s=policies["signal"]
            lines.append("- {}: {} entries, mean {} USD, PF {}".format(
                period,s["trades"],s["meanPerTradeUsd"],s["profitFactor"]))
    lines+=["","All results use ONE-MINUTE OHLC with assumed costs; only early and late",
            "TRAIN dates were considered. Results do not prove an executable edge.",""]
    (output/"es-ny-opening-range-v7.md").write_text("\n".join(lines),encoding="utf-8")
    return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--features",type=Path,required=True)
    parser.add_argument("--audit",type=Path,required=True)
    parser.add_argument("--output-dir",type=Path,required=True)
    args=parser.parse_args()
    result=run(args.features,args.audit,args.output_dir)
    print("[NinjexAI-RTH] Matched events:",result["oneEventPerDayCount"])
    print("[NinjexAI-RTH] Validation NOT scored:",result["validationFeatureRowsNotScored"])
    print("[NinjexAI-RTH] Holdout NOT scored:",result["holdoutFeatureRowsNotScored"])
    print("[NinjexAI-RTH] Report:",args.output_dir/"es-ny-opening-range-v7.json")
    return 0


if __name__=="__main__":
    raise SystemExit(main())
