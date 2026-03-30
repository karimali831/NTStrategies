namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private void RunProtectiveWatchdog()
            {
                FollowerGuard settings;
                bool armed;
                AutoFlattenProtectionScope missingBracketScope;

                lock (_gate)
                {
                    settings = _followerGuard ?? new FollowerGuard();
                    armed = Armed;
                    missingBracketScope = _autoFlattenMissingBracket;
                }

                if (settings.Enabled && armed)
                {
                    CheckFollowerEntryTimeouts(settings);
                    CheckFollowerDesyncs(settings);
                }

                if (missingBracketScope != AutoFlattenProtectionScope.Disabled)
                    CheckMissingProtectiveBracket();
                
                RunAutoBreakEvenWatchdog();
                AuditProtectionStates();
            }
        }
    }
}