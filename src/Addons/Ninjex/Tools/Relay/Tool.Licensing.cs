using System.Windows;
using NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Models;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private void RecomputeEffectiveSimMode(bool rebuildUiBindings)
        {
            var newEffectiveSimOnlyMode = !_liveModePermitted || _userSimOnlyMode;
            var changed = _simOnlyMode != newEffectiveSimOnlyMode;

            _simOnlyMode = newEffectiveSimOnlyMode;

            UpdateSimModeUiState();

            if (changed && rebuildUiBindings)
                OnSimModeChanged();
        }

        private void UpdateSimModeUiState()
        {
            if (_simModeCheckBox == null)
                return;
            
            _simModeCheckBox.Visibility = _liveModePermitted
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ApplyLicenseState(LicenseState state)
        {
            if (state == null)
                return;

            var previousEffectiveSimOnlyMode = _simOnlyMode;

            _liveModePermitted = state.CanUseLive;

            RecomputeEffectiveSimMode(rebuildUiBindings: false);

            _uiDispatcher?.InvokeAsync(() =>
            {
                UpdateSimModeUiState();

                if (_licenseStatusText != null)
                    _licenseStatusText.Text = state.StatusText ?? string.Empty;

                if (_licenseFingerprintText != null)
                    _licenseFingerprintText.Text = state.Fingerprint ?? string.Empty;

                if (_licenseVersionText != null)
                    _licenseVersionText.Text = state.AddonVersion ?? string.Empty;

                RefreshRelayStatusPanel();
                RenderFollowerRowsState();
                RenderFlattenEnablementUi();
                RenderBreakEvenEnablementUi();

                if (previousEffectiveSimOnlyMode != _simOnlyMode)
                    OnSimModeChanged();
            });
        }

        private void OnLicenseStateChanged(LicenseState state)
        {
            ApplyLicenseState(state);
        }
    }
}