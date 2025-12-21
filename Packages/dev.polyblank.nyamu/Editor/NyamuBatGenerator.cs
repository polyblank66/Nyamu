using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using System.IO;
using System.Text;
using System;
using System.Linq;

namespace Nyamu
{
#if UNITY_EDITOR_WIN

    // Automatically generates a .bat file for MCP integration on Unity load
    // The .bat file provides a convenient entry point for Claude Code MCP configuration
    static class NyamuBatGenerator
    {
        // Entry point - runs automatically when Unity Editor loads
        [InitializeOnLoadMethod]
        static void Initialize()
        {
            // Generate bat file on initial load
            GenerateBatFile();

            // Subscribe to package registration events to detect package updates
            Events.registeredPackages += OnPackagesRegistered;
        }

        // Called when packages are registered (installed/updated/removed)
        static void OnPackagesRegistered(PackageRegistrationEventArgs args)
        {
            // Check if our package was updated
            var nyamuPackage = args.added.Concat(args.changedTo)
                .FirstOrDefault(p => p.name == "dev.polyblank.nyamu");

            if (nyamuPackage != null)
            {
                NyamuLogger.LogInfo($"[Nyamu][BatGenerator] Package update detected: {nyamuPackage.version}");
                GenerateBatFile();
            }
        }

        static void GenerateBatFile()
        {
            try
            {
                var mcpServerPath = FindMcpServerPath();
                if (string.IsNullOrEmpty(mcpServerPath))
                {
                    NyamuLogger.LogWarning("[Nyamu][BatGenerator] Could not locate mcp-server.js. Bat file generation skipped.");
                    return;
                }

                var port = NyamuSettings.Instance.serverPort;
                var batContent = GenerateBatContent(mcpServerPath, port);
                WriteBatFile(batContent);
                GeneratePostmanCollection();
            }
            catch (Exception ex)
            {
                NyamuLogger.LogWarning($"[Nyamu][BatGenerator] Unexpected error during bat file generation: {ex.Message}");
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
                NyamuLogger.LogWarning($"[Nyamu][BatGenerator] Failed to locate mcp-server.js: {ex.Message}");
                return null;
            }
        }

        // Generates .bat file content with proper MCP protocol compliance
        static string GenerateBatContent(string mcpServerPath, int port)
        {
            // Determine log file path relative to project root
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var logFilePath = Path.Combine(projectRoot, ".nyamu", "mcp-server.log");

            // Normalize path for Windows (backslashes) and quote for spaces
            var normalizedLogPath = logFilePath.Replace("/", "\\");

            // @echo off prevents stdout pollution
            // Quoted paths handle spaces
            // --port parameter configures the Unity HTTP server port
            // --log-file parameter enables file-based logging
            // %* forwards all additional command-line arguments
            return $"@echo off{Environment.NewLine}node \"{mcpServerPath}\" --port {port} --log-file \"{normalizedLogPath}\" %*{Environment.NewLine}";
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
                    NyamuLogger.LogDebug($"[Nyamu][BatGenerator] Bat file already up to date: {batFilePath}");
                    return;
                }

                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                File.WriteAllText(batFilePath, content, encoding);

                NyamuLogger.LogInfo($"[Nyamu][BatGenerator] Generated bat file: {batFilePath}");
            }
            catch (Exception ex)
            {
                NyamuLogger.LogWarning($"[Nyamu][BatGenerator] Failed to write bat file: {ex.Message}");
            }
        }

        // Generates Postman collection file in .nyamu directory
        static void GeneratePostmanCollection()
        {
            try
            {
                var templatePath = FindPostmanTemplatePath();
                if (templatePath == null)
                {
                    NyamuLogger.LogWarning("[Nyamu][BatGenerator] Postman collection template not found");
                    return;
                }

                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                var projectName = Path.GetFileName(projectRoot);
                var port = NyamuSettings.Instance.serverPort;

                var collectionContent = GeneratePostmanCollectionContent(templatePath, projectName, port);
                WritePostmanCollectionFile(collectionContent, projectName);
            }
            catch (Exception ex)
            {
                NyamuLogger.LogWarning($"[Nyamu][BatGenerator] Failed to generate Postman collection: {ex.Message}");
            }
        }

        // Finds the Postman collection template path
        static string FindPostmanTemplatePath()
        {
            // Search in Packages folder (dev mode)
            var packagesPath = Path.Combine(Application.dataPath, "..", "Packages", "dev.polyblank.nyamu", "NyamuServer-API.postman_collection.json");
            if (File.Exists(packagesPath))
                return Path.GetFullPath(packagesPath);

            // Search in Library/PackageCache (production mode)
            var packageCachePath = Path.Combine(Application.dataPath, "..", "Library", "PackageCache");
            if (Directory.Exists(packageCachePath))
            {
                var nyamuPackages = Directory.GetDirectories(packageCachePath, "dev.polyblank.nyamu@*");
                foreach (var packageDir in nyamuPackages)
                {
                    var templatePath = Path.Combine(packageDir, "NyamuServer-API.postman_collection.json");
                    if (File.Exists(templatePath))
                        return templatePath;
                }
            }

            return null;
        }

        // Generates Postman collection content from template
        static string GeneratePostmanCollectionContent(string templatePath, string projectName, int port)
        {
            var template = File.ReadAllText(templatePath);

            // Replace collection name to include project name
            template = template.Replace(
                "\"name\": \"Nyamu MCP Server API\"",
                $"\"name\": \"{projectName} - Nyamu MCP Server API\""
            );

            // Set actual port value in variables section
            template = System.Text.RegularExpressions.Regex.Replace(
                template,
                "\"key\": \"nyamu-port\",\\s*\"value\": \"[^\"]*\"",
                $"\"key\": \"nyamu-port\",\n\t\t\t\"value\": \"{port}\""
            );

            return template;
        }

        // Writes Postman collection file
        static void WritePostmanCollectionFile(string content, string projectName)
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath).FullName;
                var outputDir = Path.Combine(projectRoot, ".nyamu");

                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                var collectionFilePath = Path.Combine(outputDir, $"{projectName}.postman_collection.json");

                if (!ShouldWriteFile(collectionFilePath, content))
                {
                    NyamuLogger.LogDebug($"[Nyamu][BatGenerator] Postman collection already up to date: {collectionFilePath}");
                    return;
                }

                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                File.WriteAllText(collectionFilePath, content, encoding);

                NyamuLogger.LogInfo($"[Nyamu][BatGenerator] Generated Postman collection: {collectionFilePath}");
            }
            catch (Exception ex)
            {
                NyamuLogger.LogWarning($"[Nyamu][BatGenerator] Failed to write Postman collection file: {ex.Message}");
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
