from pathlib import Path

PATH = Path("src/Strategies/NinjexOvernightEdgePortfolio/Core.cs")
s = PATH.read_text(encoding="utf-8-sig")


def replace_once(old: str, new: str, label: str) -> None:
    global s
    count = s.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    s = s.replace(old, new, 1)


replace_once(
    "using System.ComponentModel.DataAnnotations;\n",
    "using System.ComponentModel.DataAnnotations;\nusing System.Collections.Generic;\n",
    "collections using",
)

replace_once(
    'private const string StrategyVersion = "1.2.2-debug-rth";',
    'private const string StrategyVersion = "1.2.3-verified-rth";',
    "strategy version",
)

replace_once(
    "        private const int RthDiagnosticTickLimit = 5;\n\n        #endregion",
    '''        private const int RthDiagnosticTickLimit = 5;

        private sealed class RthOpenOverrideEntry
        {
            public RthOpenOverrideEntry(
                double price,
                string source)
            {
                Price = price;
                Source = source ?? string.Empty;
            }

            public readonly double Price;
            public readonly string Source;
        }

        // Only independently verified live-vs-stored-data discrepancies belong
        // here. Never add a value because it improves a historical outcome.
        private static readonly Dictionary<DateTime, RthOpenOverrideEntry>
            VerifiedRthOpenOverrides =
                new Dictionary<DateTime, RthOpenOverrideEntry>
                {
                    {
                        new DateTime(2026, 9, 24),
                        new RthOpenOverrideEntry(
                            7734.25,
                            "Observed live strategy log; downloaded Replay/Historical used 7734.00")
                    }
                };

        #endregion''',
    "verified override map",
)

replace_once(
    '''                // Blank = normal strategy behaviour.
                // Recommended diagnostic format is yyyy-MM-dd=price so an
                // override cannot accidentally leak into another session.
                DebugRthOpenOverride = string.Empty;''',
    '''                // Disabled by default. Verified overrides are intended only
                // for forensic Replay/Historical reconstruction when a live
                // RTH open discrepancy has been independently confirmed.
                EnableVerifiedRthOpenOverrides = false;''',
    "override default",
)

replace_once(
    '''                    "MaxTrades={7} MaxWinners={8} MaxLosses={9} " +
                    "DebugRthOpen='{10}'",''',
    '''                    "MaxTrades={7} MaxWinners={8} MaxLosses={9} " +
                    "VerifiedRthOpenOverrides={10}",''',
    "ready diagnostic format",
)

replace_once(
    "                    DebugRthOpenOverride ?? string.Empty);",
    "                    EnableVerifiedRthOpenOverrides);",
    "ready diagnostic arg",
)

replace_once(
    '''                double debugOverridePrice;
                string debugOverrideReason;

                var debugOverrideApplied =
                    TryGetDebugRthOpenOverride(
                        signalTime.Date,
                        out debugOverridePrice,
                        out debugOverrideReason);

                if (debugOverrideApplied)
                {
                    appliedRthOpen =
                        debugOverridePrice;
                }''',
    '''                double verifiedOverridePrice;
                string verifiedOverrideSource;
                string verifiedOverrideReason;

                var verifiedOverrideApplied =
                    TryGetVerifiedRthOpenOverride(
                        signalTime.Date,
                        out verifiedOverridePrice,
                        out verifiedOverrideSource,
                        out verifiedOverrideReason);

                if (verifiedOverrideApplied)
                {
                    appliedRthOpen =
                        verifiedOverridePrice;
                }''',
    "override lookup",
)

replace_once(
    '''                    "OverrideApplied={5} OverrideSetting='{6}' " +
                    "OverrideReason={7} " +
                    "FirstRthTickTime={8} FirstRthTickPrice={9} " +
                    "State={10}",''',
    '''                    "OverrideApplied={5} OverridesEnabled={6} " +
                    "OverrideReason={7} OverrideSource='{8}' " +
                    "FirstRthTickTime={9} FirstRthTickPrice={10} " +
                    "State={11}",''',
    "rth diagnostic format",
)

replace_once(
    '''                    debugOverrideApplied,
                    DebugRthOpenOverride ?? string.Empty,
                    debugOverrideReason,
                    firstTickTimeText,
                    firstTickPriceText,
                    State);''',
    '''                    verifiedOverrideApplied,
                    EnableVerifiedRthOpenOverrides,
                    verifiedOverrideReason,
                    verifiedOverrideSource,
                    firstTickTimeText,
                    firstTickPriceText,
                    State);''',
    "rth diagnostic args",
)

replace_once(
    '''                    debugOverrideApplied
                        ? "DebugOverride"
                        : "Captured1mOpen");''',
    '''                    verifiedOverrideApplied
                        ? "VerifiedOverride"
                        : "Captured1mOpen");''',
    "rth source label",
)

start = s.find("        private bool TryGetDebugRthOpenOverride(")
end = s.find("        private static DateTime DateTimeForTimeValue(", start)
if start < 0 or end < 0 or end <= start:
    raise RuntimeError("debug override parser block not found")

verified_method = '''        private bool TryGetVerifiedRthOpenOverride(
            DateTime tradingDate,
            out double overridePrice,
            out string source,
            out string reason)
        {
            overridePrice =
                double.NaN;

            source =
                string.Empty;

            reason =
                "Disabled";

            if (!EnableVerifiedRthOpenOverrides)
                return false;


            RthOpenOverrideEntry entry;

            if (!VerifiedRthOpenOverrides.TryGetValue(
                    tradingDate.Date,
                    out entry))
            {
                reason =
                    "NoVerifiedOverride";

                return false;
            }


            if (entry == null
                || !IsFinite(entry.Price)
                || entry.Price <= 0)
            {
                reason =
                    "InvalidVerifiedOverride";

                return false;
            }


            overridePrice =
                TickSize > 0
                    ? Math.Round(entry.Price / TickSize) * TickSize
                    : entry.Price;

            source =
                entry.Source ?? string.Empty;

            reason =
                "Applied";

            return true;
        }


'''

s = s[:start] + verified_method + s[end:]

replace_once(
    '''        [NinjaScriptProperty]
        [Display(
            Name = "Debug RTH Open Override",
            Description = "Diagnostic only. Leave blank for normal behaviour. Recommended format: yyyy-MM-dd=price (example: 2026-09-24=7734.25). A price-only value is also accepted but applies to every processed date, so date-qualified format is safer.",
            GroupName = "8. Diagnostics",
            Order = 1)]
        public string DebugRthOpenOverride
        {
            get;
            set;
        }''',
    '''        [NinjaScriptProperty]
        [Display(
            Name = "Enable Verified RTH Open Overrides",
            Description = "Applies only date-keyed RTH-open corrections that were independently verified from live data. Disabled by default. Run 5 (2025-09-16 through 2026-09-11) has no override dates.",
            GroupName = "8. Diagnostics",
            Order = 1)]
        public bool EnableVerifiedRthOpenOverrides
        {
            get;
            set;
        }''',
    "override property",
)

if "DebugRthOpenOverride" in s or "TryGetDebugRthOpenOverride" in s:
    raise RuntimeError("old debug override references remain")

if "EnableEMAFilter" not in s:
    raise RuntimeError("EMA filter toggle unexpectedly missing")

PATH.write_text(s, encoding="utf-8-sig")
print("Patched", PATH)
