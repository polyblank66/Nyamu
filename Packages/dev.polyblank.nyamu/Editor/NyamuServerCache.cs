using UnityEngine;
using System;
using System.IO;

namespace Nyamu
{
    [System.Serializable]
    public class NyamuServerCache
    {
        private const string CachePath = ".nyamu/NyamuServerCache.json";

        public string lastCompilationTime = "";
        public string lastCompilationRequestTime = "";
        public string lastTestRunTime = "";

        public static NyamuServerCache Load()
        {
            EnsureCacheDirectory();

            if (File.Exists(CachePath))
            {
                try
                {
                    var json = File.ReadAllText(CachePath);
                    var cache = JsonUtility.FromJson<NyamuServerCache>(json);

                    if (NyamuSettings.Instance.enableDebugLogs)
                        Debug.Log($"[NyamuServerCache] Loaded cache from {CachePath}");

                    return cache;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NyamuServerCache] Failed to load cache: {ex.Message}. Using defaults.");

                    // Delete corrupt file
                    try
                    {
                        File.Delete(CachePath);
                    }
                    catch { }
                }
            }
            else
            {
                if (NyamuSettings.Instance.enableDebugLogs)
                    Debug.Log("[NyamuServerCache] No cache file found, using defaults");
            }

            return new NyamuServerCache();
        }

        public static void Save(NyamuServerCache cache)
        {
            try
            {
                EnsureCacheDirectory();
                var json = JsonUtility.ToJson(cache, true);
                File.WriteAllText(CachePath, json);

                if (NyamuSettings.Instance.enableDebugLogs)
                    Debug.Log($"[NyamuServerCache] Saved cache to {CachePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NyamuServerCache] Failed to save cache: {ex.Message}");
            }
        }

        private static void EnsureCacheDirectory()
        {
            var directory = Path.GetDirectoryName(CachePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NyamuServerCache] Failed to create cache directory: {ex.Message}");
                }
            }
        }
    }
}
