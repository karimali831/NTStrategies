namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        public partial class RelayEngine
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