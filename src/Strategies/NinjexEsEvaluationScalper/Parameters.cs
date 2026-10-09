using System;
using System.ComponentModel.DataAnnotations;
namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexEsEvaluationScalper
    {
        [NinjaScriptProperty, Range(1,3), Display(Name="Contracts", GroupName="Evaluation scalper", Order=0)]
        public int Contracts { get; set; }
        [NinjaScriptProperty, Range(50,1000), Display(Name="RiskPerTrade", GroupName="Evaluation scalper", Order=1)]
        public double RiskPerTrade { get; set; }
        [NinjaScriptProperty, Range(4,40), Display(Name="MinStopTicks", GroupName="Evaluation scalper", Order=2)]
        public int MinStopTicks { get; set; }
        [NinjaScriptProperty, Range(4,80), Display(Name="MaxStopTicks", GroupName="Evaluation scalper", Order=3)]
        public int MaxStopTicks { get; set; }
        [NinjaScriptProperty, Range(.5,4), Display(Name="RewardRisk", GroupName="Evaluation scalper", Order=4)]
        public double RewardRisk { get; set; }
        [NinjaScriptProperty, Range(100,1900), Display(Name="DailyLossLimit", GroupName="Evaluation scalper", Order=5)]
        public double DailyLossLimit { get; set; }
        [NinjaScriptProperty, Range(100,3000), Display(Name="DailyProfitLimit", GroupName="Evaluation scalper", Order=6)]
        public double DailyProfitLimit { get; set; }
        [NinjaScriptProperty, Range(1,50), Display(Name="MaxTradesPerDay", GroupName="Evaluation scalper", Order=7)]
        public int MaxTradesPerDay { get; set; }
        [NinjaScriptProperty, Range(0,1800), Display(Name="CooldownSeconds", GroupName="Evaluation scalper", Order=8)]
        public int CooldownSeconds { get; set; }
        [NinjaScriptProperty, Range(30,1800), Display(Name="MaxHoldSeconds", GroupName="Evaluation scalper", Order=9)]
        public int MaxHoldSeconds { get; set; }
        [NinjaScriptProperty, Range(1,20), Display(Name="SweepTicks", GroupName="Evaluation scalper", Order=10)]
        public int SweepTicks { get; set; }
        [NinjaScriptProperty, Range(1,8), Display(Name="ReclaimTicks", GroupName="Evaluation scalper", Order=11)]
        public int ReclaimTicks { get; set; }
        [NinjaScriptProperty, Range(1,20), Display(Name="BreakoutTicks", GroupName="Evaluation scalper", Order=12)]
        public int BreakoutTicks { get; set; }
        [NinjaScriptProperty, Range(0,8), Display(Name="RetestTicks", GroupName="Evaluation scalper", Order=13)]
        public int RetestTicks { get; set; }
        [NinjaScriptProperty, Range(1,10), Display(Name="ConfirmTicks", GroupName="Evaluation scalper", Order=14)]
        public int ConfirmTicks { get; set; }
        [NinjaScriptProperty, Range(10,600), Display(Name="SetupExpirySeconds", GroupName="Evaluation scalper", Order=15)]
        public int SetupExpirySeconds { get; set; }
        [NinjaScriptProperty, Range(0,50), Display(Name="CommissionPerSide", GroupName="Evaluation scalper", Order=16)]
        public double CommissionPerSide { get; set; }
        [NinjaScriptProperty, Display(Name="ExportRawTicks", GroupName="Evaluation scalper", Order=17)]
        public bool ExportRawTicks { get; set; }
        [NinjaScriptProperty, Display(Name="AllowLiveAccounts", GroupName="Evaluation scalper", Order=18)]
        public bool AllowLiveAccounts { get; set; }
        [NinjaScriptProperty, Display(Name="FirstTradeDate", GroupName="Evaluation scalper", Order=19)]
        public DateTime FirstTradeDate { get; set; }
        [NinjaScriptProperty, Display(Name="LastTradeDate", GroupName="Evaluation scalper", Order=20)]
        public DateTime LastTradeDate { get; set; }
        [NinjaScriptProperty, Display(Name="OutputFolder", GroupName="Evaluation scalper", Order=21)]
        public string OutputFolder { get; set; }
        [NinjaScriptProperty, Display(Name="Stop at evaluation boundary (disable for continuous research)", GroupName="Evaluation scalper", Order=23)]
        public bool EnforceEvaluationLimits { get; set; }
        private string ParameterSummary()
        {
            return string.Join(";", new object[] {
"EnforceEvaluationLimits=" + EnforceEvaluationLimits.ToString(),"Contracts=" + Contracts.ToString(),"RiskPerTrade=" + RiskPerTrade.ToString(),"MinStopTicks=" + MinStopTicks.ToString(),"MaxStopTicks=" + MaxStopTicks.ToString(),"RewardRisk=" + RewardRisk.ToString(),"DailyLossLimit=" + DailyLossLimit.ToString(),"DailyProfitLimit=" + DailyProfitLimit.ToString(),"MaxTradesPerDay=" + MaxTradesPerDay.ToString(),"CooldownSeconds=" + CooldownSeconds.ToString(),"MaxHoldSeconds=" + MaxHoldSeconds.ToString(),"SweepTicks=" + SweepTicks.ToString(),"ReclaimTicks=" + ReclaimTicks.ToString(),"BreakoutTicks=" + BreakoutTicks.ToString(),"RetestTicks=" + RetestTicks.ToString(),"ConfirmTicks=" + ConfirmTicks.ToString(),"SetupExpirySeconds=" + SetupExpirySeconds.ToString(),"CommissionPerSide=" + CommissionPerSide.ToString(),"ExportRawTicks=" + ExportRawTicks.ToString(),"AllowLiveAccounts=" + AllowLiveAccounts.ToString(),"FirstTradeDate=" + FirstTradeDate.ToString(),"LastTradeDate=" + LastTradeDate.ToString(),"OutputFolder=" + OutputFolder.ToString() });
        }
    }
}
