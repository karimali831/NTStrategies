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
            private void CheckMissingProtectiveBracket()
            {
                Instrument instr;
                Account master;
                List<Account> followers;
                AutoFlattenProtectionScope scope;

                lock (_gate)
                {
                    instr = _instrument;
                    master = _master;
                    followers = _followers?.ToList() ?? new List<Account>();
                    scope = _autoFlattenMissingBracket;
                }

                if (instr == null || scope == AutoFlattenProtectionScope.Disabled)
                    return;

                var accounts = new List<Account>();
                if (master != null)
                    accounts.Add(master);

                accounts.AddRange(followers.Where(a => a != null));
                accounts = accounts
                    .GroupBy(a => a.Name ?? "", StringComparer.Ordinal)
                    .Select(g => g.First())
                    .ToList();

                foreach (var acc in accounts)
                {
                    if (acc == null)
                        continue;

                    if (!IsProtectionEnabledForAccount(acc, scope))
                        continue;

                    if (!HasSelectedBracket(acc))
                        continue;

                    if (!TryGetLivePosition(acc, instr, out _, out _))
                        continue;

                    if (HasWorkingProtectiveStop(acc, instr))
                        continue;

                    // follower entry still being resolved: let normal entry timeout logic handle that window
                    if (!ReferenceEquals(acc, _master))
                    {
                        var state = GetGuardState(acc);
                        if (state != null && state.EntryWorking)
                            continue;
                    }

                    TriggerRiskProtectionFlatten(
                        acc,
                        instr,
                        "Live position detected with selected bracket but no working protective stop.");
                }
            }
        }
    }
}