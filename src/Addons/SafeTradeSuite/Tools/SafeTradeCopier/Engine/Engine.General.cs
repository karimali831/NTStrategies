using System;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private bool AllowCopyNow()
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-2).Ticks;
                while (_copiedTicks.TryPeek(out var t) && t < cutoff)
                    _copiedTicks.TryDequeue(out _);

                return _copiedTicks.Count <= MaxCopiesPer2Sec;
            }
            
            private void RecordCopy()
            {
                _copiedTicks.Enqueue(DateTime.UtcNow.Ticks);
            }
            
            
            public void SetCopyEnabled(bool enabled)
            {
                lock (_gate)
                {
                    if (enabled)
                    {
                        if (!IsReady_NoLock(out var reason))
                        {
                            _copyEnabled = false;
                            DisarmUnsafe_NoLock("COPY ON blocked");
                            RaiseModeChanged_NoLock();
                            RaiseReady_NoLock(reasonOverride: reason);
                            Log($"COPY ON blocked: {reason}");
                            return;
                        }

                        _copyEnabled = true;
                        RewireUnsafe_NoLock("COPY ON");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock();
                        Log("COPY ON.");
                    }
                    else
                    {
                        _copyEnabled = false;
                        DisarmUnsafe_NoLock("COPY OFF");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock();
                        Log("COPY OFF.");
                    }
                }
            }
        }
    }
}