#!/usr/bin/env python3
"""ES 21-EMA H1/H2 pullback research, TRAIN only (NOT tick or live fills).

Completed 5m EMA21 trend and stateful H1 -> failed first attempt -> H2.
Orders: signal bar +/-1 tick STOP trigger, opposite extreme +/-1 tick
protective stop, 2R target, 5m pending lifetime, 30m maximum hold.
Adverse one-tick slippage each side; $5 fee, max $250 total nominal
risk. Minute OHLC stop-first assumptions are not tick-accurate.
"""
import argparse
import datetime as dt
import json
import math
from collections import Counter, deque
from pathlib import Path

import numpy as np
import pandas as pd

if __package__:
    from tools.ai_market_ml_features import NY
    from tools.ai_market_train_baseline import read_verified_csv
    from tools.ai_market_bracket_research import summarize
    from tools.ai_market_frozen_reverse_v6 import read_raw_before
else:
    from ai_market_ml_features import NY
    from ai_market_train_baseline import read_verified_csv
    from ai_market_bracket_research import summarize
    from ai_market_frozen_reverse_v6 import read_raw_before

TICK = 0.25
POINT_VALUE = 50.
EMA_LENGTH = 21
ATR_LENGTH = 14
WARMUP = 55
SLOPE_LOOKBACK = 5
MAX_SETUP_BARS = 12
RISK_CAP_USD = 250.
TARGET_R = 2.
PENDING_MINUTES = 5
HOLD_MINUTES = 30
FUTURE_MINUTES = PENDING_MINUTES + HOLD_MINUTES
FEE = 5.
SLIP_TICKS = 1
TRAIN_END = dt.date(2025,9,1)
EARLY_END = dt.date(2024,9,1)
RAW_END = dt.datetime(2025,9,1,tzinfo=NY).astimezone(dt.timezone.utc)


def norm(bar,side):
    return ((bar.open,bar.high,bar.low,bar.close) if side==1
            else (-bar.open,-bar.low,-bar.high,-bar.close))


def strong(bar,side):
    op,hi,lo,cl=norm(bar,side)
    width=hi-lo
    return width>=TICK and cl>op and (cl-op)/width>=.5 and (cl-lo)/width>=.7


def record(kind,bar,side,ema,ema5,atr,start,index,failed):
    op,hi,lo,cl=norm(bar,side)
    return {
        "type":kind,"side":"LONG" if side==1 else "SHORT",
        "decisionUtc":bar.end.isoformat(),
        "entryTrigger":round(bar.high+TICK if side==1 else bar.low-TICK,2),
        "stopPrice":round(bar.low-TICK if side==1 else bar.high+TICK,2),
        "nominalStopTicks":int(round((hi-lo+2*TICK)/TICK)),
        "atr14":round(atr,6),"ema21":round(ema,6),
        "emaSlope5Atr":round(side*(ema-ema5)/atr,6),
        "closeFromEmaAtr":round((cl-side*ema)/atr,6),
        "bodyFraction":round((cl-op)/(hi-lo),6),
        "closeLocation":round((cl-lo)/(hi-lo),6),
        "pullbackBars":index-start,
        "failedFirstDepthAtr":round(failed,6) if failed is not None else None,
    }


class SecondEntryState:
    """Only past and CURRENT completed 5m bars can move the state."""
    def __init__(self,side):
        self.side=side
        self.reset()

    def reset(self):
        self.stage="SEEK"
        self.impulse=False
        self.start=None
        self.first_hi=None
        self.first_lo=None
        self.failure_low=None

    def on_bar(self,bar,prior,ema,ema5,atr,index):
        s=self.side
        op,hi,lo,cl=norm(bar,s)
        po,ph,pl,pc=norm(prior,s)
        e=s*ema
        if atr<=0 or not math.isfinite(atr):
            self.reset()
            return None
        # Fixed trend: EMA rises >=0.05 ATR over 5 bars, mirror for shorts.
        if (s*(ema-ema5))/atr < .05 or cl < e-.5*atr:
            self.reset()
            return None
        if self.stage!="SEEK" and index-self.start>MAX_SETUP_BARS:
            self.reset()
        if self.stage=="SEEK":
            if cl>=e+.5*atr:
                self.impulse=True
            if self.impulse and lo<=e+.25*atr and cl<pc:
                self.stage="FIRST"
                self.start=index
            return None
        if self.stage=="FIRST":
            if hi>=ph+TICK and strong(bar,s):
                self.stage="FAIL"
                self.first_hi=hi
                self.first_lo=lo
                return record("H1",bar,s,ema,ema5,atr,self.start,index,None)
            return None
        if self.stage=="FAIL":
            breakout=hi>=self.first_hi+TICK
            lower=lo<=self.first_lo-TICK
            if breakout:
                # If breakout and failure share one 5m bar, order unknown.
                self.reset()
            elif lower:
                self.stage="SECOND"
                self.failure_low=lo
            return None
        if self.stage=="SECOND":
            self.failure_low=min(self.failure_low,lo)
            if hi>=ph+TICK and strong(bar,s):
                depth=max(0.,(self.first_lo-self.failure_low)/atr)
                out=record("H2",bar,s,ema,ema5,atr,self.start,index,depth)
                self.reset()
                return out
            return None
        raise ValueError("Unknown H1/H2 state")


def detect(bars):
    """Streaming 5m detector, reset on gaps and NY calendar boundaries."""
    states={1:SecondEntryState(1),-1:SecondEntryState(-1)}
    previous=None
    ema=None
    past_ema=deque(maxlen=SLOPE_LOOKBACK+1)
    trs=deque(maxlen=ATR_LENGTH)
    warm=0
    day=None
    events=[]
    resets=0
    for b in bars:
        if previous is not None and b.end<=previous.end:
            raise ValueError("Raw 5m bars duplicated or reversed")
        gap=(previous is not None and b.end-previous.end!=dt.timedelta(minutes=5))
        if gap or "INVALID" in b.flags:
            for st in states.values(): st.reset()
            past_ema.clear()
            trs.clear()
            ema=None
            warm=0
            resets+=1
        if "INVALID" in b.flags:
            previous=b
            continue
        tr=(max(b.high-b.low,abs(b.high-previous.close),abs(b.low-previous.close))
            if previous and not gap and "INVALID" not in previous.flags
            else b.high-b.low)
        trs.append(tr)
        ema=b.close if ema is None else ema+2/(EMA_LENGTH+1)*(b.close-ema)
        past_ema.append(ema)
        warm+=1
        eastern=b.end.astimezone(NY)
        if eastern.date()!=day:
            for st in states.values(): st.reset()
            day=eastern.date()
        clock=eastern.hour*60+eastern.minute
        if (previous is not None and warm>=WARMUP
                and len(past_ema)==SLOPE_LOOKBACK+1 and
                len(trs)==ATR_LENGTH and eastern.weekday()<5
                and 9*60+30<clock<=15*60):
            atr=sum(trs)/len(trs)
            for side in (1,-1):
                found=states[side].on_bar(b,previous,ema,past_ema[0],atr,warm)
                if found:
                    events.append(found)
        previous=b
    return events,resets


def future_minutes(one,t):
    bars=[one.get(t+dt.timedelta(minutes=i))
          for i in range(1,FUTURE_MINUTES+1)]
    return None if any(b is None or "INVALID" in b.flags for b in bars) else bars


def simulate_pending(candidate,bars):
    """No orders until after closed signal candle. STOP within 5 minutes.

    A gap may enlarge entry loss; post-gap oversized risks are marked
    unsafe, NOT guaranteed cancellable for a real resting stop order.
    Same-minute stop/target ambiguity resolves to stop first.
    """
    if len(bars)!=FUTURE_MINUTES:
        raise ValueError("Need complete 35-minute source path")
    side=1 if candidate["side"]=="LONG" else -1
    level=candidate["entryTrigger"]
    stop=candidate["stopPrice"]
    if side*(level-stop)<=0:
        raise ValueError("Invalid signal stop geometry")
    for i,b in enumerate(bars[:PENDING_MINUTES]):
        if not (b.high>=level if side==1 else b.low<=level):
            continue
        gap=(b.open>=level if side==1 else b.open<=level)
        ref=b.open if gap else level
        fill=ref+side*TICK*SLIP_TICKS
        risk=side*(fill-stop)
        worst_cost=risk*POINT_VALUE+TICK*POINT_VALUE+FEE
        if worst_cost>RISK_CAP_USD:
            return {"filled":False,"reason":"unsafeGapOrRisk"}
        target=fill+side*TARGET_R*risk
        exit_price,reason,ambiguous=None,None,False
        exit_i=i+HOLD_MINUTES-1
        for j in range(i,i+HOLD_MINUTES):
            c=bars[j]
            hit_stop=(c.low<=stop if side==1 else c.high>=stop)
            hit_target=(c.high>=target if side==1 else c.low<=target)
            if hit_stop:
                if j>i and (c.open<=stop if side==1 else c.open>=stop):
                    exit_price,reason=c.open,"STOP_GAP"
                elif hit_target:
                    exit_price,reason,ambiguous=stop,"AMBIGUOUS_STOP_FIRST",True
                else:
                    exit_price,reason=stop,"STOP"
            elif hit_target:
                exit_price,reason=target,"TARGET"
            if reason:
                exit_i=j
                break
        if reason is None:
            exit_price,reason=bars[exit_i].close,"TIME"
        exit_fill=exit_price-side*TICK*SLIP_TICKS
        return {
            "filled":True,"entryUtc":bars[i].end.isoformat(),
            "exitUtc":bars[exit_i].end.isoformat(),
            "entryFill":round(fill,2),"exitFill":round(exit_fill,2),
            "netUsd":round(side*(exit_fill-fill)*POINT_VALUE-FEE,2),
            "initialStopRiskUsd":round(worst_cost,2),
            "targetPrice":round(target,2),"exitReason":reason,
            "bothTouched1m":ambiguous,"gapEntry":bool(gap)
        }
    return {"filled":False,"reason":"noFillWithinFiveMinutes"}


def fixed_selection(candidates):
    """Ex-ante limits per H1/H2 independent experiment; not portfolio."""
    selected=[]
    rejects=Counter()
    daily=Counter()
    last={}
    for e in sorted(candidates,key=lambda x:(x["decisionUtc"],x["side"])):
        nominal=e["nominalStopTicks"]*TICK*POINT_VALUE+2*TICK*POINT_VALUE+FEE
        if nominal>RISK_CAP_USD:
            rejects["nominalRiskAbove250Usd"]+=1
            continue
        key=e["type"]
        if daily[(key,e["easternDate"])]>=2:
            rejects["twoSignalDailyLimit"]+=1
            continue
        instant=dt.datetime.fromisoformat(e["decisionUtc"])
        if key in last and instant-last[key]<dt.timedelta(minutes=35):
            rejects["fixed35MinuteSeparation"]+=1
            continue
        selected.append(e)
        daily[(key,e["easternDate"])]+=1
        last[key]=instant
    return selected,dict(sorted(rejects.items()))


def stats(rows):
    return summarize([{"decisionUtc":r["decisionUtc"],
                       "month":r["month"],"netUsd":r["netUsd"],
                       "exitReason":r["exitReason"],
                       "bothTouched1m":r["bothTouched1m"]}
                      for r in rows])


def analyze(features,audit,output):
    manifest=json.loads(features.with_suffix(".manifest.json").read_text("utf-8-sig"))
    basket=json.loads(audit.read_text("utf-8-sig"))
    if basket.get("schema")!="ninjex-basket-audit-v1" or (
            basket.get("datasetTag")!=manifest.get("datasetTag")):
        raise ValueError("Feature/basket provenance mismatch")
    all_data=read_verified_csv(features,manifest)
    counts={x:int((all_data["researchSplit"]==x).sum())
            for x in ("train","validation","holdout")}
    train=all_data.loc[all_data["researchSplit"]=="train"].copy()
    del all_data
    train["utcClose"]=pd.to_datetime(train["decisionUtc"],utc=True)
    train["day"]=train["utcClose"].dt.tz_convert(NY).dt.date
    if (train["day"]>=TRAIN_END).any():
        raise ValueError("TRAIN date crosses validation")
    clashes=train.groupby("day")["contract"].nunique()
    overlapping=set(clashes.loc[clashes>1].index)
    sources={m["contract"]:Path(m["folders"][0]) for m in basket["contracts"]
             if len(m.get("folders",[]))==1}
    candidates=[]
    excluded=Counter()
    raw_resets=0
    for contract,group in train.groupby("contract",sort=True):
        if contract not in sources:
            raise ValueError("Missing one unique source: "+contract)
        folder=sources[contract]
        source_meta=json.loads((folder/"manifest.json").read_text("utf-8-sig"))
        if source_meta.get("contract")!=contract or (
                source_meta.get("datasetTag")!=basket["datasetTag"]):
            raise ValueError("Raw dated contract/tag mismatch")
        five=read_raw_before(folder,5,contract,RAW_END)
        by_close=group.set_index("utcClose")
        events,resets=detect(list(five.values()))
        raw_resets+=resets
        for event in events:
            stamp=dt.datetime.fromisoformat(event["decisionUtc"])
            day=stamp.astimezone(NY).date()
            if day in overlapping:
                excluded["rolloverOverlappingContracts"]+=1
                continue
            if day>=TRAIN_END:
                raise ValueError("Event crossed TRAIN boundary")
            if pd.Timestamp(stamp) not in by_close.index:
                excluded["missingAsOfFeature"]+=1
                continue
            entry=by_close.loc[pd.Timestamp(stamp)]
            if isinstance(entry,pd.DataFrame):
                excluded["duplicateAsOfFeature"]+=1
                continue
            if pd.to_numeric(entry["fine1mComplete"],errors="coerce")!=1 or (
                    entry["qualityFlags"]!="OK"):
                excluded["invalidFeatureQuality"]+=1
                continue
            if abs(float(entry["close"])-five[stamp].close)>1e-7:
                raise ValueError("Feature/raw 5m close mismatch")
            event.update({
                "contract":contract,"easternDate":day.isoformat(),
                "month":day.strftime("%Y-%m"),
                "period":"EARLY_TRAIN" if day<EARLY_END else "LATE_TRAIN",
                "clockEtMinute":stamp.astimezone(NY).hour*60+stamp.astimezone(NY).minute,
                "volumeVsPrior20":float(entry["volumeVsPrior20"]),
                "efficiency12":float(entry["efficiency12"])
            })
            candidates.append(event)
    # Zero candidates is a legitimate negative research finding.
    chosen,selection_rejected=fixed_selection(candidates)
    outcomes=[]
    execution_rejected=Counter()
    for contract in sorted({x["contract"] for x in chosen}):
        minutes=read_raw_before(sources[contract],1,contract,RAW_END)
        for event in (x for x in chosen if x["contract"]==contract):
            stamp=dt.datetime.fromisoformat(event["decisionUtc"])
            if stamp+dt.timedelta(minutes=FUTURE_MINUTES)>=RAW_END:
                execution_rejected["crossTrainBoundary"]+=1
                continue
            future=future_minutes(minutes,stamp)
            if future is None:
                execution_rejected["missingFutureMinute"]+=1
                continue
            outcome=simulate_pending(event,future)
            if not outcome["filled"]:
                execution_rejected[outcome["reason"]]+=1
                continue
            outcomes.append({**event,**outcome})
    outcomes.sort(key=lambda x:x["decisionUtc"])
    breakdown={}
    for kind in ("H1","H2"):
        current=[x for x in outcomes if x["type"]==kind]
        breakdown[kind]={
            "overall":stats(current),
            "bySide":{side:stats([x for x in current if x["side"]==side])
                      for side in ("LONG","SHORT")},
            "byPeriod":{period:stats([x for x in current if x["period"]==period])
                        for period in ("EARLY_TRAIN","LATE_TRAIN")},
            "medianInitialStopRiskUsd":round(
                float(np.median([x["initialStopRiskUsd"] for x in current])),2)
                if current else None
        }
    result={
        "schema":"ninjex-es-second-entry-21ema-v10",
        "status":"TRAIN_ONLY_MECHANICAL_BENCHMARK_NOT_AI_DISCOVERY",
        "sourceStatus":manifest.get("researchStatus"),
        "trainingFeatureRows":counts["train"],
        "validationRowsNotScored":counts["validation"],
        "finalHoldoutRowsNotScored":counts["holdout"],
        "rawAfterSep2025NotLoaded":True,
        "frozenDefinition":{
            "chart":"completed ES ETH 5m, NOT a tick chart",
            "trend":"EMA21 slope over 5 bars >= 0.05 ATR14, earlier close 0.5ATR beyond EMA",
            "pullback":"low/high reaches EMA within 0.25ATR, lower/higher close, not beyond EMA by >0.5ATR",
            "H1_L1":"first strong higher-high/lower-low bar after EMA pullback",
            "firstFailure":"later bar extends 1tick beyond H1 pullback low/high without simultaneously passing H1 high/low",
            "H2_L2":"later strong higher-high/lower-low after failure, within 12 bars total",
            "strongBar":"directional body>=50pct range, close in top/bottom 30pct",
            "signalWindow":"09:35 through 15:00 ET, EMA warmup on uninterrupted earlier ETH bars",
            "entry":"pending stop +1 tick beyond completed signal bar, five 1m bars",
            "stop":"1 tick beyond opposite end of signal bar",
            "target":"2 times actual filled entry-to-stop risk",
            "maxHoldMinutesAfterFill":HOLD_MINUTES,
            "maxNominalRiskUsd":RISK_CAP_USD,
            "slippageTicksEachSide":SLIP_TICKS,
            "roundTripCommissionUsd":FEE,
            "ambiguousMinute":"STOP FIRST",
            "maxSignalsEachCohortPerDay":2,
            "minimumSignalSpacingMinutes":35
        },
        "rawGapResets":raw_resets,
        "asOfCandidateCounts":dict(sorted(Counter(x["type"] for x in candidates).items())),
        "sourceExcluded":dict(sorted(excluded.items())),
        "preExecutionSelected":len(chosen),
        "riskOrThrottleRejected":selection_rejected,
        "unfilledOrInvalidOutcome":dict(sorted(execution_rejected.items())),
        "filledTrades":len(outcomes),
        "results":breakdown,
        "warnings":[
            "Rule-based H1/H2 BENCHMARK: this is not AI-discovered profit.",
            "Failed first entry is a price-action event, not a verified filled and losing H1 order.",
            "H1/H2 separately throttled cohorts cannot be aggregated as simultaneous portfolio PnL.",
            "Gap beyond pending stop-entry may fill outside risk cap; simulated rejection is NOT guaranteed broker cancellation.",
            "Requires full 35-minute future 1m OHLC even if exit earlier, no interpolation.",
            "Minute OHLC cannot establish intraminute fill order or accurately reproduce 21-EMA tick-chart patterns.",
            "Closed-equity drawdown is not prop-firm intraday unrealized trailing drawdown.",
            "Only TRAIN is examined, previously used validation and final 2026 holdout untouched.",
            "Data source remains provisional and needs dated contract audit."
        ]
    }
    output.mkdir(parents=True,exist_ok=True)
    (output/"es-second-entry-v10.json").write_text(
        json.dumps(result,indent=2),encoding="utf-8")
    csv_columns=["type","side","decisionUtc","contract","easternDate","month","period",
                 "clockEtMinute","entryTrigger","stopPrice","nominalStopTicks",
                 "atr14","ema21","emaSlope5Atr","closeFromEmaAtr","bodyFraction",
                 "closeLocation","pullbackBars","failedFirstDepthAtr",
                 "volumeVsPrior20","efficiency12","filled","entryUtc","exitUtc",
                 "entryFill","exitFill","netUsd","initialStopRiskUsd",
                 "targetPrice","exitReason","bothTouched1m","gapEntry"]
    pd.DataFrame(outcomes,columns=csv_columns).to_csv(
        output/"es-second-entry-v10-events.csv",index=False)
    lines=["# Ninjex AI v10 — mechanical 21-EMA H1/H2 research",
           "","TRAIN ONLY. Mechanically defined hypothesis, not AI-discovered alpha.",
           "",
           "As-of candidates: {}; risk-approved pending: {}; filled: {}.".format(
               len(candidates),len(chosen),len(outcomes)),
           "Earlier validation untouched: {}; final holdout untouched: {}.".format(
               counts["validation"],counts["holdout"]),
           "",
           "| Cohort | Trades | Net USD | Mean USD | PF | Closed DD |",
           "|---|---:|---:|---:|---:|---:|"]
    for kind,item in breakdown.items():
        s=item["overall"]
        lines.append("| {} | {} | {} | {} | {} | {} |".format(
            kind,s["trades"],s["netUsd"],s["meanPerTradeUsd"],
            s["profitFactor"],s["maxClosedEquityDrawdownUsd"]))
    lines+=["","## Early vs late TRAIN (NOT validation)",""]
    for kind,item in breakdown.items():
        for period,s in item["byPeriod"].items():
            lines.append("- {} {}: {} trades, mean {} USD, PF {}".format(
                kind,period,s["trades"],s["meanPerTradeUsd"],s["profitFactor"]))
    lines+=["","Pending orders and brackets are 1m OHLC approximations, not tick fills.",
            "A positive subgroup on TRAIN cannot be claimed as a verified edge.",""]
    (output/"es-second-entry-v10.md").write_text("\n".join(lines),encoding="utf-8")
    return result


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--features",type=Path,required=True)
    p.add_argument("--audit",type=Path,required=True)
    p.add_argument("--output-dir",type=Path,required=True)
    a=p.parse_args()
    r=analyze(a.features,a.audit,a.output_dir)
    print("[NinjexAI-V10] trades:",r["filledTrades"])
    print("[NinjexAI-V10] validation untouched:",r["validationRowsNotScored"])
    print("[NinjexAI-V10] holdout untouched:",r["finalHoldoutRowsNotScored"])
    print("[NinjexAI-V10] report:",a.output_dir/"es-second-entry-v10.json")
    return 0


if __name__=="__main__":
    raise SystemExit(main())
