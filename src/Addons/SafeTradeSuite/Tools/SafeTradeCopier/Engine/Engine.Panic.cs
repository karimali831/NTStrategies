using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            public void EmergencyStopConfiguredAccounts()
            {
                List<Account> accounts;

                lock (_gate)
                {
                    _isRequested = false;
                    DisarmUnsafe_NoLock("Emergency stop");
                    RaiseModeChanged_NoLock();
                    RaiseReady_NoLock(reasonOverride: "Emergency stop active");

                    accounts = new List<Account>();

                    if (_configuredMaster != null)
                        accounts.Add(_configuredMaster);

                    if (_configuredFollowers != null)
                        accounts.AddRange(_configuredFollowers.Where(a => a != null));
                }

                accounts = accounts
                    .Where(a => a != null)
                    .GroupBy(a => a.Name ?? "", StringComparer.Ordinal)
                    .Select(g => g.First())
                    .ToList();

                Log($"[PANIC] Emergency stop initiated. Accounts={accounts.Count}");

                foreach (var acc in accounts)
                {
                    try
                    {
                        var instruments = CollectActiveInstruments(acc);

                        if (instruments.Count == 0)
                        {
                            Log($"[PANIC] no active instruments -> {acc.Name}");
                            continue;
                        }

                        foreach (var instr in instruments)
                        {
                            try
                            {
                                EnsureFlatInstrument(acc, instr, FlattenTriggerReason.Panic);
                                Log($"[PANIC] flatten requested -> acc={acc.Name} instr={instr.FullName}");
                            }
                            catch (Exception ex)
                            {
                                Log(
                                    $"[PANIC] flatten failed -> acc={acc?.Name} instr={instr?.FullName} msg={ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[PANIC] account scan failed -> acc={acc?.Name} msg={ex.Message}");
                    }
                }
            }
        }
    }
}