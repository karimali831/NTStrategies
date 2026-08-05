using System;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts
{
    public sealed class CandidateSignal
    {
        public string CandidateId { get; }
        public string BreakoutEventId { get; }
        public string ModelId { get; }
        public string ModelVersion { get; }

        public TradeDirection Direction { get; }
        public DateTime SignalTime { get; }
        public int SignalBarIndex { get; }

        public double RangeLevel { get; }
        public double StructuralStopPrice { get; }
        public CandleSnapshot ConfirmationCandle { get; }
        public CandleMetrics Metrics { get; }
        public CandidateFeatureSnapshot Features { get; }
        public CandidateQualificationSnapshot Qualification { get; }

        public CandidateModelDetails ModelDetails { get; }
        public bool IsQualified { get; }
        public string QualificationCode { get; }
        public string QualificationReason { get; }

        public CandidateSignal(
            string candidateId,
            string breakoutEventId,
            string modelId,
            string modelVersion,
            TradeDirection direction,
            DateTime signalTime,
            int signalBarIndex,
            double rangeLevel,
            double structuralStopPrice,
            CandleSnapshot confirmationCandle,
            CandleMetrics metrics,
            CandidateFeatureSnapshot features,
            CandidateQualificationSnapshot qualification,
            CandidateModelDetails modelDetails,
            bool isQualified,
            string qualificationCode,
            string qualificationReason)
        {
            CandidateId = candidateId;
            BreakoutEventId = breakoutEventId;
            ModelId = modelId;
            ModelVersion = modelVersion;
            Direction = direction;
            SignalTime = signalTime;
            SignalBarIndex = signalBarIndex;
            RangeLevel = rangeLevel;
            StructuralStopPrice = structuralStopPrice;
            ConfirmationCandle = confirmationCandle;
            Metrics = metrics;
            Features = features ?? CandidateFeatureSnapshot.Empty;

            Qualification =
                qualification
                ?? CandidateQualificationSnapshot.Passed;

            ModelDetails =
                modelDetails
                ?? CandidateModelDetails.Empty;

            IsQualified = isQualified;
            QualificationCode = qualificationCode ?? string.Empty;
            QualificationReason = qualificationReason ?? string.Empty;
        }
    }
}