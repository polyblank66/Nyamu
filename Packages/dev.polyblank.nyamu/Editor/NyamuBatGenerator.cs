using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System;

namespace Nyamu
{
#if UNITY_EDITOR_WIN

    // Automatically generates a .bat file for MCP integration on Unity load
    // The .bat file provides a convenient entry point for Claude Code MCP configuration
    static class NyamuBatGenerator
    {
        // Entry point - runs automatically when Unity Editor loads
        [InitializeOnLoadMethod]
        static void GenerateBatFile()
        {
            try
            {
                var mcpServerPath = FindMcpServerPath();
                if (string.IsNullOrEmpty(mcpServerPath))
                {
                    NyamuLog.Warning("[Nyamu][BatGenerator] Could not locate mcp-server.js. Bat file generation skipped.");
                    return;
                }

                var port = NyamuSettings.Instance.serverPort;
                var batContent = GenerateBatContent(mcpServerPath, port);
                WriteBatFile(batContent);
            }
            catch (Exception ex)
            {
                NyamuLog.Warning($"[Nyamu][BatGenerator] Unexpected error during bat file generation: {ex.Message}");
            }
        }

        // Public method to regenerate bat file (called when settings change)
        public static void RegenerateBatFile()
        {
            GenerateBatFile();
        }

        // Locates mcp-server.js in either PackageCache or embedded package
        static string FindMcpServerPath()
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;

                // Search PackageCache first (production)
                var packageCacheRoot = Path.Combine(projectRoot, "Library", "PackageCache");
                if (Directory.Exists(packageCacheRoot))
                {
                    var packageDirs = Directory.GetDirectories(packageCacheRoot, "dev.polyblank.nyamu@*");
                    foreach (var packageDir in packageDirs)
                    {
                        var cachedPath = Path.Combine(packageDir, "Node", "mcp-server.js");
                        if (File.Exists(cachedPath))
                            return cachedPath;
                    }
                }

                // Check embedded package last (dev mode)
                var embeddedPath = Path.Combine(projectRoot, "Packages", "dev.polyblank.nyamu", "Node", "mcp-server.js");
                if (File.Exists(embeddedPath))
                    return embeddedPath;

                return null;
            }
            catch (Exception ex)
            {
                NyamuLog.Warning($"[Nyamu][BatGenerator] Failed to locate mcp-server.js: {ex.Message}");
                return null;
            }
        }

        // Generates .bat file content with proper MCP protocol compliance
        static string GenerateBatContent(string mcpServerPath, int port)
        {
            // @echo off prevents stdout pollution
            // Quoted path handles spaces
            // --port parameter configures the Unity HTTP server port
            // %* forwards all additional command-line arguments
            return $"@echo off{Environment.NewLine}node \"{mcpServerPath}\" --port {port} %*{Environment.NewLine}";
        }

        // Writes .bat file to .nyamu/nyamu.bat (idempotent)
        static void WriteBatFile(string content)
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                var outputDir = Path.Combine(projectRoot, ".nyamu");
                Directory.CreateDirectory(outputDir);

                var batFilePath = Path.Combine(outputDir, "nyamu.bat");

                if (!ShouldWriteFile(batFilePath, content))
                {
                    NyamuLog.Debug($"[Nyamu][BatGenerator] Bat file already up to date: {batFilePath}");
                    return;
                }

                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                File.WriteAllText(batFilePath, content, encoding);

                NyamuLog.Info($"[Nyamu][BatGenerator] Generated bat file: {batFilePath}");
            }
            catch (Exception ex)
            {
                NyamuLog.Warning($"[Nyamu][BatGenerator] Failed to write bat file: {ex.Message}");
            }
        }

        // Checks if file needs to be written (idempotency)
        static bool ShouldWriteFile(string filePath, string newContent)
        {
            if (!File.Exists(filePath))
                return true;

            try
            {
                var existingContent = File.ReadAllText(filePath);
                return existingContent != newContent;
            }
            catch
            {
                return true;
            }
        }
    }

#endif
}
