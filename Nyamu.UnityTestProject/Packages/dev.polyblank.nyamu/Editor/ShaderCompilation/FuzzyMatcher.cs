using System;
using System.Collections.Generic;
using System.Linq;
using Nyamu.Tools.Shaders;

namespace Nyamu.ShaderCompilation
{
    // Fuzzy string matching utility for shader name search
    public static class FuzzyMatcher
    {
        public static int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
            if (string.IsNullOrEmpty(target)) return source.Length;

            var n = source.Length;
            var m = target.Length;
            var d = new int[n + 1, m + 1];

            for (var i = 0; i <= n; i++) d[i, 0] = i;
            for (var j = 0; j <= m; j++) d[0, j] = j;

            for (var i = 1; i <= n; i++)
            {
                for (var j = 1; j <= m; j++)
                {
                    var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }

        public static int CalculateMatchScore(string query, string target)
        {
            var queryLower = query.ToLower();
            var targetLower = target.ToLower();

            if (queryLower == targetLower) return 100;

            if (targetLower.Contains(queryLower))
            {
                var baseScore = 90;
                var suffixLength = targetLower.Length - queryLower.Length;
                var suffixPenalty = Math.Min(suffixLength, 15);
                return baseScore - suffixPenalty;
            }

            var distance = LevenshteinDistance(queryLower, targetLower);
            var maxLength = Math.Max(query.Length, target.Length);
            var similarity = 1.0 - ((double)distance / maxLength);
            return (int)(similarity * 80);
        }

        public static List<ShaderMatch> FindBestMatches(string query, string[] shaderNames, string[] shaderPaths, int maxResults = 5)
        {
            var matches = new List<ShaderMatch>();

            for (var i = 0; i < shaderNames.Length; i++)
            {
                var score = CalculateMatchScore(query, shaderNames[i]);
                if (score > 30)
                {
                    matches.Add(new ShaderMatch
                    {
                        name = shaderNames[i],
                        path = shaderPaths[i],
                        matchScore = score
                    });
                }
            }

            matches.Sort((a, b) => b.matchScore.CompareTo(a.matchScore));
            return matches.Take(maxResults).ToList();
        }
    }
}
