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

                    NyamuLogger.LogDebug($"[Nyamu][ServerCache] Loaded cache from {CachePath}");

                    return cache;
                }
                catch (Exception ex)
                {
                    NyamuLogger.LogWarning($"[Nyamu][ServerCache] Failed to load cache: {ex.Message}. Using defaults.");

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
                NyamuLogger.LogDebug("[Nyamu][ServerCache] No cache file found, using defaults");
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

                NyamuLogger.LogDebug($"[Nyamu][ServerCache] Saved cache to {CachePath}");
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][ServerCache] Failed to save cache: {ex.Message}");
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
                    NyamuLogger.LogError($"[Nyamu][ServerCache] Failed to create cache directory: {ex.Message}");
                }
            }
        }
    }
}
