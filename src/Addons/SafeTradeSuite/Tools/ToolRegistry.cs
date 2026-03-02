#region Using declarations
using System;
using System.Collections.Generic;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools
{
    public sealed class ToolRegistry : IDisposable
    {
        private readonly Dictionary<string, Func<object>> factories = new Dictionary<string, Func<object>>(StringComparer.Ordinal);
        private readonly Dictionary<string, object> instances = new Dictionary<string, object>(StringComparer.Ordinal);

        public void Register<T>(string key, Func<T> factory) where T : class
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required", nameof(key));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            factories[key] = () => factory();
        }

        public T Get<T>(string key) where T : class
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required", nameof(key));

            if (instances.TryGetValue(key, out var existing))
                return existing as T;

            if (!factories.TryGetValue(key, out var create))
                throw new InvalidOperationException($"Tool not registered: '{key}'");

            var obj = create();
            instances[key] = obj;
            return obj as T;
        }

        public void Dispose()
        {
            foreach (var kv in instances)
            {
                if (kv.Value is IDisposable d)
                    d.Dispose();
            }

            instances.Clear();
            factories.Clear();
        }
    }
}