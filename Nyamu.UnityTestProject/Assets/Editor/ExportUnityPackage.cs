using UnityEditor;
using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;

public static class ExportUnityPackage
{
    // === SETTINGS ===
    private const string PackageName = "dev.polyblank.nyamu";

    // Path to the external package
    private const string ExternalPackagePath = "../Nyamu.UnityPackage";

    // Temporary folder inside Assets
    private const string TempEmbeddedPath = "Assets/Nyamu";

    // === ENTRY POINT ===
    [MenuItem("Tools/Export UnityPackage")]
    public static void Export()
    {
        try
        {
            EmbedToTemp();

            var version = ReadVersionFromPackageJson(TempEmbeddedPath);
            var outputDir = "Artifacts";
            Directory.CreateDirectory(outputDir);

            var outputPath = $"{outputDir}/{PackageName}-{version}.unitypackage";

            AssetDatabase.ExportPackage(
                TempEmbeddedPath,
                outputPath,
                ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies
            );

            Debug.Log($"[CI] Exported unitypackage: {outputPath}");
        }
        finally
        {
            CleanupTemp();
        }
    }

    // === IMPLEMENTATION ===

    private static void EmbedToTemp()
    {
        var source = Path.GetFullPath(ExternalPackagePath);
        var target = Path.GetFullPath(TempEmbeddedPath);

        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"External package not found: {source}");

        // Clear folder if it exists
        if (Directory.Exists(target))
            ClearDirectory(target);
        else
            Directory.CreateDirectory(target);

        CopyDirectoryRecursive(source, target);

        AssetDatabase.Refresh();
    }

    private static string ReadVersionFromPackageJson(string packagePath)
    {
        var packageJsonPath = Path.Combine(packagePath, "package.json");

        if (!File.Exists(packageJsonPath))
            throw new FileNotFoundException("package.json not found", packageJsonPath);

        var json = File.ReadAllText(packageJsonPath);
        var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");

        if (!match.Success)
            throw new System.Exception("Failed to parse version from package.json");

        return match.Groups[1].Value;
    }

    private static void CleanupTemp()
    {
        var fullTempPath = Path.GetFullPath(TempEmbeddedPath);
        var metaPath = fullTempPath + ".meta"; // путь к Nyamu.meta

        if (Directory.Exists(fullTempPath))
        {
            ClearDirectory(fullTempPath);
            Directory.Delete(fullTempPath, true);
            Debug.Log($"[CI] Cleaned temp embedded package: {TempEmbeddedPath}");
        }

        // Remove .meta file if it exists
        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
            Debug.Log($"[CI] Deleted meta file: {metaPath}");
        }

        AssetDatabase.Refresh();
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, targetFile, true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var targetSubDir = Path.Combine(targetDir, Path.GetFileName(directory));
            CopyDirectoryRecursive(directory, targetSubDir);
        }
    }

    // Clears all files and subfolders from a directory
    private static void ClearDirectory(string dir)
    {
        foreach (var file in Directory.GetFiles(dir))
            File.Delete(file);

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            ClearDirectory(subDir);
            Directory.Delete(subDir, true);
        }
    }
}
