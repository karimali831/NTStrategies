#!/usr/bin/env python3
"""Ninjex AI v9: price-independent ES opportunity/cost audit, TRAIN only.

After failed v4-v8 hypotheses, inspect opportunity rather than
optimize another entry. Every 30 minutes between 10:00 and 15:30
America/New_York, on a complete 5m decision, simulate BOTH sides
using the FROZEN 12tick stop, 24tick target, 30m time exit, 1tick
adverse slip per side and $5 commission. Any hindsight best-side
result is a NON-EXECUTABLE ORACLE UPPER BOUND, never a strategy.
No ML, parameter search, order execution, validation or holdout.
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

END_ET = dt.date(2025, 9, 1)
EARLY_ET = dt.date(2024, 9, 1)
RAW_END = dt.datetime(2025, 9, 1, tzinfo=NY).astimezone(dt.timezone.utc)
CLOCKS = frozenset(range(600, 931, 30))
COST_USD = 30.


def period(date):
    return "EARLY_TRAIN" if date < EARLY_ET else "LATE_TRAIN"


def segment(clock):
    if clock < 720:
        return "10_TO_12_ET"
    if clock < 840:
        return "12_TO_14_ET"
    return "14_TO_16_ET"


def atr_bucket(value):
    if not np.isfinite(value) or value <= 0:
        return "UNKNOWN"
    if value < 2:
        return "ATR_BELOW_2_POINTS"
    if value < 4:
        return "ATR_2_TO_4"
    if value < 8:
        return "ATR_4_TO_8"
    return "ATR_AT_LEAST_8"


def priced_cohort(train):
    frame=train.copy()
    frame["utcClose"]=pd.to_datetime(frame["decisionUtc"],utc=True)
    eastern=frame["utcClose"].dt.tz_convert(NY)
    frame["dateET"]=eastern.dt.date
    frame["minuteET"]=eastern.dt.hour*60+eastern.dt.minute
    if any(x >= END_ET for x in frame["dateET"]):
        raise ValueError("TRAIN data includes validation/holdout")
    return frame.loc[frame["minuteET"].isin(CLOCKS)].copy()


def source_rows(cohort, audit, dataset_tag):
    source={c["contract"]:Path(c["folders"][0]) for c in audit["contracts"]
            if len(c.get("folders", []))==1}
    duplicate=cohort.groupby("dateET")["contract"].nunique()
    bad_dates=set(duplicate.loc[duplicate>1].index)
    excluded=Counter()
    rows=[]
    for contract, group in cohort.groupby("contract",sort=True):
        if contract not in source:
            raise ValueError("Missing one unique source for "+contract)
        folder=source[contract]
        meta=json.loads((folder/"manifest.json").read_text("utf-8-sig"))
        if meta.get("contract")!=contract or meta.get("datasetTag")!=dataset_tag:
            raise ValueError("Raw source manifest and basket mismatch")
        # Reader breaks before 2025-09-01 ET, never opens later OHLC.
        fives=read_raw_before(folder,5,contract,RAW_END)
        ones=read_raw_before(folder,1,contract,RAW_END)
        for _,decision in group.iterrows():
            day=decision["dateET"]
            if day in bad_dates or day.weekday()>=5:
                excluded["multiContractOrWeekend"]+=1
                continue
            stamp=decision["utcClose"].to_pydatetime()
            if stamp+dt.timedelta(minutes=30)>=RAW_END:
                excluded["crossTrainBoundary"]+=1
                continue
            if pd.to_numeric(decision["fine1mComplete"],errors="coerce")!=1 or (
                    str(decision["qualityFlags"])!="OK"):
                excluded["invalidAsOfFeature"]+=1
                continue
            five=fives.get(stamp)
            if five is None or "INVALID" in five.flags:
                excluded["missingOrInvalidFiveMinute"]+=1
                continue
            if abs(five.close-float(decision["close"]))>1e-7:
                raise ValueError("Raw 5m close vs derived feature mismatch")
            future=minute_window(ones,stamp)
            if future is None:
                excluded["missingOrInvalidFuture1m"]+=1
                continue
            long=simulate(future,1,12,24)
            short=simulate(future,-1,12,24)
            ref=future[0].open
            hi=max(c.high for c in future)
            lo=min(c.low for c in future)
            ln=float(long["netUsd"])
            sn=float(short["netUsd"])
            rows.append({
                "decisionUtc":stamp.isoformat(), "dateET":day.isoformat(),
                "month":day.strftime("%Y-%m"),"contract":contract,
                "period":period(day),
                "segment":segment(int(decision["minuteET"])),
                "clockET":int(decision["minuteET"]),
                "atrBand":atr_bucket(float(decision["atr14_5m"])),
                "longUsd":ln,"shortUsd":sn,"oracleBestUsd":max(ln,sn),
                "anyPositive":int(max(ln,sn)>0),
                "bothNegative":int(ln<0 and sn<0),
                "futureRangePts":hi-lo,
                "absThirtyMinCloseMovePts":abs(future[-1].close-ref),
                "longExit":long["exitReason"],"shortExit":short["exitReason"],
                "longAmbiguous":bool(long["bothTouched1m"]),
                "shortAmbiguous":bool(short["bothTouched1m"]),
            })
    rows.sort(key=lambda r:r["decisionUtc"])
    if len(rows)!=len({(r["contract"],r["decisionUtc"]) for r in rows}):
        raise ValueError("Duplicate dated-contract decisions")
    return rows,dict(sorted(excluded.items()))


def policy_summary(rows,key):
    return summarize([{
        "decisionUtc":r["decisionUtc"],"month":r["month"],
        "netUsd":r[key],"exitReason":"HISTORICAL_SIMULATION",
        "bothTouched1m":False} for r in rows])


def describe(rows):
    n=len(rows)
    if not n:
        return {"count":0}
    return {
        "count":n,
        "days":len({r["dateET"] for r in rows}),
        "long":policy_summary(rows,"longUsd"),
        "short":policy_summary(rows,"shortUsd"),
        "oracleBestNOTExecutable":policy_summary(rows,"oracleBestUsd"),
        "atLeastOneProfitableSideRate":round(sum(r["anyPositive"] for r in rows)/n,5),
        "bothSidesLoseRate":round(sum(r["bothNegative"] for r in rows)/n,5),
        "meanRawRangePoints":round(float(np.mean([r["futureRangePts"] for r in rows])),3),
        "meanAbsThirtyMinuteCloseMovePoints":round(float(
            np.mean([r["absThirtyMinCloseMovePts"] for r in rows])),3),
        "meanLongBeforeAssumedCostsUsd":round(float(
            np.mean([r["longUsd"]+COST_USD for r in rows])),3),
        "meanShortBeforeAssumedCostsUsd":round(float(
            np.mean([r["shortUsd"]+COST_USD for r in rows])),3)
    }


def run(features,audit,output):
    meta=json.loads(features.with_suffix(".manifest.json").read_text("utf-8-sig"))
    basket=json.loads(audit.read_text("utf-8-sig"))
    if basket.get("schema")!="ninjex-basket-audit-v1" or (
            basket.get("datasetTag")!=meta.get("datasetTag")):
        raise ValueError("Feature and basket source audit tags do not match")
    all_rows=read_verified_csv(features,meta)
    counts={k:int((all_rows["researchSplit"]==k).sum())
            for k in ("train","validation","holdout")}
    train=all_rows.loc[all_rows["researchSplit"]=="train"].copy()
    del all_rows
    cohort=priced_cohort(train)
    records,excluded=source_rows(cohort,basket,meta["datasetTag"])
    if not records:
        raise ValueError("No valid clock-based historical RTH decisions")
    groups={}
    for key in ("period","segment","atrBand"):
        groups[key]={str(value):describe([r for r in records if r[key]==value])
                     for value in sorted({r[key] for r in records})}
    result={
        "schema":"ninjex-ai-tradeability-v9",
        "status":"TRAIN_ONLY_DIAGNOSTIC_NO_TRADABLE_EDGE",
        "sourceAuditStatus":basket.get("status"),
        "trainingRowsAvailable":counts["train"],
        "validationRowsNotScored":counts["validation"],
        "finalHoldoutRowsNotScored":counts["holdout"],
        "rawAfterSep1_2025ETNeverLoaded":True,
        "deterministicCandidateCloses":len(cohort),
        "sourceCompleteDecisions":len(records),
        "excluded":excluded,
        "fixedProcedure":{
            "clock":"every 30 min, 10:00 through 15:30 America/New_York, completed 5m bar",
            "signals":"NONE, entry timestamps independent of price",
            "stopTicks":12,"targetTicks":24,"maxHoldMinutes":30,
            "entry":"next raw one-minute OHLC open",
            "slippageTicksEachSide":1,"roundTripFeeUsd":5,
            "totalAssumedCostUsd":COST_USD,
            "bothTouchedWithinOneMinute":"STOP FIRST",
            "futureSourceGap":"EXCLUDE, never interpolate",
            "oracleWarning":"Choose best realized LONG/SHORT only AFTER seeing both futures; NON-EXECUTABLE"
        },
        "allTraining":describe(records),
        "fixedDescriptions":groups,
        "objectiveContext":{
            "aspirationalNetUsdPerYear":10000,
            "exampleTwoHundredTradesAnnualMeanNetRequiredUsd":50,
            "approximateMaxPropIntradayDrawdownUsd":2000,
            "dataDoNotCertifyIntradayDrawdown":True
        },
        "warnings":[
            "This is an opportunity upper-bound audit, NOT a strategy or model.",
            "The hindsight best LONG/SHORT side uses future price outcomes and must NEVER be traded or called predictive.",
            "Predeclared Eastern times and volatility buckets are descriptive; no picking profitable subgroups from this run.",
            "Only early and late TRAIN are evaluated; prior-used validation and final June2026 onward holdout remain unscored.",
            "Historical source is 1m OHLC; ambiguous stop/target resolution assumes stop-first and broker tick fills cannot be verified.",
            "Raw 30m range may contain prices after a hypothetical trade stopped out.",
            "Gross figures are reported by adding back the assumed $30 cost, not by rerunning fills at zero slippage.",
            "Closed equity drawdown is not intraday open-equity trailing drawdown.",
            "Data audit remains provisional; missing candles and invalid source flags are rejected."
        ]
    }
    output.mkdir(parents=True,exist_ok=True)
    (output/"es-tradeability-v9.json").write_text(
        json.dumps(result,indent=2),encoding="utf-8")
    pd.DataFrame(records).to_csv(output/"es-tradeability-v9-decisions.csv",index=False)
    s=result["allTraining"]
    lines=[
        "# Ninjex AI v9 — ES opportunity and cost diagnostic",
        "",
        "**Training only. No ML model, real trades, validation or holdout outcomes.**",
        "",
        "Deterministic 30-minute RTH source decisions: {}; complete: {}.".format(
            len(cohort),len(records)),
        "Validation excluded: {}; reserved holdout excluded: {}.".format(
            counts["validation"],counts["holdout"]),
        "",
        "| Baseline | Observations | Net USD | Mean/trade | PF | Closed drawdown |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for name,key in (("Always LONG","long"),("Always SHORT","short"),
                     ("Hindsight best (NOT executable)","oracleBestNOTExecutable")):
        p=s[key]
        lines.append("| {} | {} | {} | {} | {} | {} |".format(
            name,p["trades"],p["netUsd"],p["meanPerTradeUsd"],
            p["profitFactor"],p["maxClosedEquityDrawdownUsd"]))
    lines += [
        "",
        "At least one after-cost profitable side: {:.1f}%.".format(
            100*s["atLeastOneProfitableSideRate"]),
        "Both sides negative: {:.1f}%.".format(100*s["bothSidesLoseRate"]),
        "Mean 30-minute observed high-low range: {} ES points.".format(
            s["meanRawRangePoints"]),
        "Unconditional mean LONG/SHORT before assumed $30 costs: {} / {} USD.".format(
            s["meanLongBeforeAssumedCostsUsd"],
            s["meanShortBeforeAssumedCostsUsd"]),
        "",
        "## Chronological training-year comparison","",
        "| Period | Count | LONG mean | SHORT mean | Either side profitable |",
        "|---|---:|---:|---:|---:|",
    ]
    for name,p in groups["period"].items():
        lines.append("| {} | {} | {} | {} | {} |".format(
            name,p["count"],p["long"]["meanPerTradeUsd"],
            p["short"]["meanPerTradeUsd"],p["atLeastOneProfitableSideRate"]))
    lines += [
        "",
        "The oracle is an ex-post opportunity ceiling, not a strategy.",
        "Physical time/ATR strata in JSON are descriptive, not tuned entry rules.",
        "No tick-level execution or prop intraday drawdown verification.",""
    ]
    (output/"es-tradeability-v9.md").write_text("\n".join(lines),encoding="utf-8")
    return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--features",type=Path,required=True)
    parser.add_argument("--audit",type=Path,required=True)
    parser.add_argument("--output-dir",type=Path,required=True)
    a=parser.parse_args()
    r=run(a.features,a.audit,a.output_dir)
    print("[NinjexAI-V9] Complete intervals:",r["sourceCompleteDecisions"])
    print("[NinjexAI-V9] Validation NOT scored:",r["validationRowsNotScored"])
    print("[NinjexAI-V9] Holdout NOT scored:",r["finalHoldoutRowsNotScored"])
    print("[NinjexAI-V9] Report:",a.output_dir/"es-tradeability-v9.json")
    return 0


if __name__=="__main__":
    raise SystemExit(main())
