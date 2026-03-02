#region Using declarations
using System;
using System.Collections.Generic;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools
{
    public sealed class ToolRegistry : IDisposable
    {
        private readonly Dictionary<string, Func<object>> _factories =
            new Dictionary<string, Func<object>>(StringComparer.Ordinal);

        private readonly Dictionary<string, object> _instances =
            new Dictionary<string, object>(StringComparer.Ordinal);

        private readonly object _gate = new object();

        public void RegisterSingleton<T>(string key, Func<T> factory) where T : class
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required", nameof(key));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            lock (_gate)
            {
                _factories[key] = factory;
            }
        }

        public T Get<T>(string key) where T : class
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required", nameof(key));

            lock (_gate)
            {
                if (_instances.TryGetValue(key, out var existing))
                    return existing as T;

                if (!_factories.TryGetValue(key, out var create))
                    throw new InvalidOperationException($"Tool not registered: '{key}'");

                var obj = create();
                _instances[key] = obj;
                return obj as T;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                foreach (var kv in _instances)
                {
                    if (kv.Value is IDisposable d)
                        d.Dispose();
                }

                _instances.Clear();
                _factories.Clear();
            }
        }
    }
}