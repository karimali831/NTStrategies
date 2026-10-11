powershell

cd C:\Projects\NTStrategies\src\Tools\Audit-NinjaTraderTickData

V1:
 .\Audit-NinjaTraderTickData.ps1
 
V2:
.\Audit-NinjaTraderData-v2.0.ps1 -Symbols ES -From '2026-08-27' -To '2026-09-01'


V3:

.\Audit-NinjaTraderData-v3.1.ps1 `
    -Symbols ES `
    -From '2023-09-11' `
    -To '2026-10-11'