using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Nyamu.Tools.Shaders;

namespace Nyamu.ShaderCompilation
{
    // High-level shader compilation services
    public static class ShaderCompilationService
    {
        public static CompileShaderResponse CompileSingleShader(string queryName)
        {
            try
            {
                var shaderGuids = AssetDatabase.FindAssets("t:Shader");
                var shaderNames = new List<string>();
                var shaderPaths = new List<string>();

                foreach (var guid in shaderGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if (shader != null)
                    {
                        shaderNames.Add(shader.name);
                        shaderPaths.Add(path);
                    }
                }

                var matches = FuzzyMatcher.FindBestMatches(queryName, shaderNames.ToArray(), shaderPaths.ToArray(), 5);

                if (matches.Count == 0)
                {
                    return new CompileShaderResponse
                    {
                        status = "error",
                        message = $"No shaders found matching '{queryName}'",
                        allMatches = new ShaderMatch[0]
                    };
                }

                var bestMatch = matches[0];

                EditorUtility.DisplayProgressBar(
                    "Compiling Shader",
                    $"Compiling: {bestMatch.name}",
                    0.5f
                );

                var compileResult = ShaderCompiler.CompileShaderAtPath(bestMatch.path);

                return new CompileShaderResponse
                {
                    status = compileResult.hasErrors ? "error" : "ok",
                    message = matches.Count > 1
                        ? $"Found {matches.Count} matches. Auto-selected best match: {bestMatch.name}"
                        : $"Compiled shader: {bestMatch.name}",
                    allMatches = matches.ToArray(),
                    bestMatch = bestMatch,
                    result = compileResult
                };
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][ShaderCompilation] Shader compilation failed: {ex.Message}");
                return new CompileShaderResponse
                {
                    status = "error",
                    message = $"Shader compilation failed: {ex.Message}"
                };
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static CompileAllShadersResponse CompileAllShaders()
        {
            var startTime = DateTime.Now;
            var results = new List<ShaderCompileResult>();

            try
            {
                var shaderGuids = AssetDatabase.FindAssets("t:Shader");
                NyamuLogger.LogInfo($"[Nyamu][ShaderCompilation] Compiling {shaderGuids.Length} shaders...");

                // Initialize progress tracking
                Server.UpdateAllShadersProgress(shaderGuids.Length, 0, "");

                for (var i = 0; i < shaderGuids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(shaderGuids[i]);
                    NyamuLogger.LogInfo($"[Nyamu][ShaderCompilation] Compiling shader {i + 1}/{shaderGuids.Length}: {path}");

                    // Update progress tracking
                    Server.UpdateAllShadersProgress(shaderGuids.Length, i, path);

                    var result = ShaderCompiler.CompileShaderAtPath(path);
                    results.Add(result);

                    EditorUtility.DisplayProgressBar(
                        "Compiling Shaders",
                        $"Compiling shader {i + 1}/{shaderGuids.Length}: {Path.GetFileName(path)}",
                        (float)(i + 1) / shaderGuids.Length
                    );
                }

                var totalTime = (DateTime.Now - startTime).TotalSeconds;

                // Mark progress as complete
                Server.UpdateAllShadersProgress(shaderGuids.Length, shaderGuids.Length, "");
                var successCount = results.Count(r => !r.hasErrors);
                var failCount = results.Count(r => r.hasErrors);

                return new CompileAllShadersResponse
                {
                    status = failCount > 0 ? "warning" : "ok",
                    totalShaders = results.Count,
                    successfulCompilations = successCount,
                    failedCompilations = failCount,
                    totalCompilationTime = totalTime,
                    results = results.ToArray()
                };
            }
            catch (Exception ex)
            {
                NyamuLogger.LogError($"[Nyamu][ShaderCompilation] Compile all shaders failed: {ex.Message}");
                return new CompileAllShadersResponse
                {
                    status = "error",
                    totalShaders = 0,
                    successfulCompilations = 0,
                    failedCompilations = 0,
                    totalCompilationTime = 0,
                    results = new ShaderCompileResult[0]
                };
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static CompileShadersRegexResponse CompileShadersRegex(string pattern)
        {
            try
            {
                var regex = new System.Text.RegularExpressions.Regex(pattern);
                var allShaderGuids = AssetDatabase.FindAssets("t:Shader");
                var matchingShaders = new List<string>();

                foreach (var guid in allShaderGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (regex.IsMatch(path))
                        matchingShaders.Add(path);
                }

                if (matchingShaders.Count == 0)
                {
                    return new CompileShadersRegexResponse
                    {
                        status = "ok",
                        message = "No shaders matched the pattern",
                        pattern = pattern,
                        totalShaders = 0,
                        successfulCompilations = 0,
                        failedCompilations = 0,
                        totalCompilationTime = 0,
                        results = new ShaderCompileResult[0]
                    };
                }

                NyamuLogger.LogInfo($"[Nyamu][ShaderCompilation] Compiling {matchingShaders.Count} shaders matching pattern: {pattern}");

                var results = new List<ShaderCompileResult>();
                var startTime = DateTime.Now;
                var successCount = 0;
                var failCount = 0;

                // Initialize progress tracking via Server statics (keep coupling for now)
                Server.UpdateRegexShadersProgress(pattern, matchingShaders.Count, 0, "");

                for (var i = 0; i < matchingShaders.Count; i++)
                {
                    var shaderPath = matchingShaders[i];
                    NyamuLogger.LogInfo($"[Nyamu][ShaderCompilation] Compiling shader {i + 1}/{matchingShaders.Count}: {shaderPath}");

                    // Update progress tracking
                    Server.UpdateRegexShadersProgress(pattern, matchingShaders.Count, i, shaderPath);

                    var result = ShaderCompiler.CompileShaderAtPath(shaderPath);
                    results.Add(result);

                    if (result.hasErrors)
                        failCount++;
                    else
                        successCount++;

                    EditorUtility.DisplayProgressBar(
                        "Compiling Shaders (Regex)",
                        $"Compiling shader {i + 1}/{matchingShaders.Count}: {Path.GetFileName(shaderPath)}",
                        (float)(i + 1) / matchingShaders.Count
                    );
                }

                var totalTime = (DateTime.Now - startTime).TotalSeconds;

                // Mark progress as complete
                Server.UpdateRegexShadersProgress(pattern, matchingShaders.Count, matchingShaders.Count, "");

                return new CompileShadersRegexResponse
                {
                    status = failCount > 0 ? "warning" : "ok",
                    message = $"Compiled {results.Count} shaders matching pattern",
                    pattern = pattern,
                    totalShaders = results.Count,
                    successfulCompilations = successCount,
                    failedCompilations = failCount,
                    totalCompilationTime = totalTime,
                    results = results.ToArray()
                };
            }
            catch (Exception ex)
            {
                return new CompileShadersRegexResponse
                {
                    status = "error",
                    message = $"Failed to compile shaders: {ex.Message}",
                    pattern = pattern,
                    totalShaders = 0,
                    successfulCompilations = 0,
                    failedCompilations = 0,
                    totalCompilationTime = 0,
                    results = new ShaderCompileResult[0]
                };
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
