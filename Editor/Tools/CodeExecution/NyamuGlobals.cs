using System.Collections.Generic;

namespace Nyamu.CodeExecution
{
    // Cross-execution state bag for code_execute snippets. Cleared on every domain reload
    // (Server.Cleanup calls Clear()) and whenever the Editor restarts - do not rely on it
    // surviving a script compile or a Play Mode transition.
    public static class NyamuGlobals
    {
        static readonly Dictionary<string, object> Values = new();
        static readonly object Lock = new();

        public static void Set(string key, object value)
        {
            lock (Lock) Values[key] = value;
        }

        public static object Get(string key)
        {
            lock (Lock) return Values.TryGetValue(key, out var value) ? value : null;
        }

        public static T Get<T>(string key)
        {
            lock (Lock) return Values.TryGetValue(key, out var value) && value is T typed ? typed : default;
        }

        public static bool Has(string key)
        {
            lock (Lock) return Values.ContainsKey(key);
        }

        public static string[] Keys()
        {
            lock (Lock)
            {
                var keys = new string[Values.Count];
                Values.Keys.CopyTo(keys, 0);
                return keys;
            }
        }

        public static void Clear()
        {
            lock (Lock) Values.Clear();
        }
    }
}
