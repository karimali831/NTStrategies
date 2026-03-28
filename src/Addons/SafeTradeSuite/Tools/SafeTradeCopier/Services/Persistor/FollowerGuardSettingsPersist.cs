using System;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private FollowerGuard _lastAppliedFollowerGuardSettings;
        
        private void LoadFollowerGuardSettingsFromState()
        {
            var settings = _persistedState?.FollowerShield ?? new FollowerShieldSettings();

            _followerGuardEnabled = settings.Enabled;
            _followerGuardEntryFillTimeoutSeconds = settings.EntryFillTimeoutSeconds > 0
                ? settings.EntryFillTimeoutSeconds
                : 5;
            _followerGuardDesyncGraceSeconds = settings.DesyncGraceSeconds > 0
                ? settings.DesyncGraceSeconds
                : 3;

            _followerGuardOnEntryReject = settings.OnEntryReject;
            _followerGuardOnEntryTimeout = settings.OnEntryTimeout;
            _followerGuardOnDesync = settings.OnDesync;
        }

        private void SaveFollowerGuardSettingsToState()
        {
            EnsurePersistedStateDefaults();

            _persistedState.FollowerShield.Enabled = _followerGuardEnabled;
            _persistedState.FollowerShield.EntryFillTimeoutSeconds = _followerGuardEntryFillTimeoutSeconds;
            _persistedState.FollowerShield.DesyncGraceSeconds = _followerGuardDesyncGraceSeconds;
            _persistedState.FollowerShield.OnEntryReject = _followerGuardOnEntryReject;
            _persistedState.FollowerShield.OnEntryTimeout = _followerGuardOnEntryTimeout;
            _persistedState.FollowerShield.OnDesync = _followerGuardOnDesync;
        }

        private void LoadFollowerGuardSettingsIntoUi()
        {
            _isLoadingFollowerGuardUi = true;

            try
            {
                if (_fgEnabledCheckBox != null)
                    _fgEnabledCheckBox.IsChecked = _followerGuardEnabled;

                if (_fgEntryTimeoutTextBox != null)
                    _fgEntryTimeoutTextBox.Text = Math.Max(1, _followerGuardEntryFillTimeoutSeconds).ToString();

                if (_fgDesyncGraceTextBox != null)
                    _fgDesyncGraceTextBox.Text = Math.Max(1, _followerGuardDesyncGraceSeconds).ToString();

                if (_fgOnEntryRejectComboBox != null)
                    _fgOnEntryRejectComboBox.SelectedItem = _followerGuardOnEntryReject;

                if (_fgOnEntryTimeoutComboBox != null)
                    _fgOnEntryTimeoutComboBox.SelectedItem = _followerGuardOnEntryTimeout;

                if (_fgOnDesyncComboBox != null)
                    _fgOnDesyncComboBox.SelectedItem = _followerGuardOnDesync;
            }
            finally
            {
                _isLoadingFollowerGuardUi = false;
            }
        }

        private void ApplyFollowerGuardSettingsFromUi()
        {
            _followerGuardEnabled = _fgEnabledCheckBox?.IsChecked == true;
            _followerGuardEntryFillTimeoutSeconds = ParseIntOrDefault(_fgEntryTimeoutTextBox?.Text, 5, 1, 300);
            _followerGuardDesyncGraceSeconds = ParseIntOrDefault(_fgDesyncGraceTextBox?.Text, 3, 1, 120);
            _followerGuardOnEntryReject = GetSelectedGuardAction(_fgOnEntryRejectComboBox, GuardAction.FlattenAndDisable);
            _followerGuardOnEntryTimeout = GetSelectedGuardAction(_fgOnEntryTimeoutComboBox, GuardAction.FlattenAndDisable);
            _followerGuardOnDesync = GetSelectedGuardAction(_fgOnDesyncComboBox, GuardAction.FlattenAndDisable);

            SaveFollowerGuardSettingsToState();
            SavePersistentUiState();
            ApplyFollowerGuardSettingsToEngine();
        }

        private void ApplyFollowerGuardSettingsToEngine()
        {
            var settings = new FollowerGuard
            {
                Enabled = _followerGuardEnabled,
                EntryFillTimeoutSeconds = _followerGuardEntryFillTimeoutSeconds,
                DesyncGraceSeconds = _followerGuardDesyncGraceSeconds,
                OnEntryReject = _followerGuardOnEntryReject,
                OnEntryTimeout = _followerGuardOnEntryTimeout,
                OnDesync = _followerGuardOnDesync
            };

            if (_lastAppliedFollowerGuardSettings != null &&
                _lastAppliedFollowerGuardSettings.Enabled == settings.Enabled &&
                _lastAppliedFollowerGuardSettings.EntryFillTimeoutSeconds == settings.EntryFillTimeoutSeconds &&
                _lastAppliedFollowerGuardSettings.DesyncGraceSeconds == settings.DesyncGraceSeconds &&
                _lastAppliedFollowerGuardSettings.OnEntryReject == settings.OnEntryReject &&
                _lastAppliedFollowerGuardSettings.OnEntryTimeout == settings.OnEntryTimeout &&
                _lastAppliedFollowerGuardSettings.OnDesync == settings.OnDesync)
            {
                return;
            }

            _lastAppliedFollowerGuardSettings = new FollowerGuard
            {
                Enabled = settings.Enabled,
                EntryFillTimeoutSeconds = settings.EntryFillTimeoutSeconds,
                DesyncGraceSeconds = settings.DesyncGraceSeconds,
                OnEntryReject = settings.OnEntryReject,
                OnEntryTimeout = settings.OnEntryTimeout,
                OnDesync = settings.OnDesync
            };

            _engine?.UpdateFollowerGuardSettings(settings);
        }
    }
}