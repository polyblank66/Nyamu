using UnityEditor;
using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;

// Lives in Nyamu.UnityPackageExporter, a project that deliberately does NOT reference
// dev.polyblank.nyamu. Exporting embeds a copy of the package under Assets/, and if the
// same package were also installed under Packages/, Unity would see every asset GUID
// twice and rewrite the .meta files to resolve the collision - corrupting the GUIDs the
// released package ships with. Keeping the exporter in a package-free project means the
// copied .meta files are imported exactly as they are stored in the repository.
public static class ExportUnityPackage
{
    // === SETTINGS ===
    private const string PackageName = "dev.polyblank.nyamu";

    // Path to the external package
    private const string ExternalPackagePath = "../Nyamu.UnityPackage";

    // Temporary folder inside Assets
    private const string TempEmbeddedPath = "Assets/Nyamu";

    // Package folders that must not ship inside the .unitypackage.
    // Tests/ is meant for UPM consumers, who opt into it through "testables" in their
    // manifest.json. A .unitypackage unpacks into Assets/, where "testables" has no
    // effect, so these tests would show up uninvited in the consumer's Test Runner.
    // They stay in the repository and keep running in Nyamu.UnityTestProject.
    private static readonly string[] ExcludedFromExport = { "Tests" };

    // === ENTRY POINT ===
    [MenuItem("Tools/Export UnityPackage")]
    public static void Export()
    {
        try
        {
            EmbedToTemp();
            PruneExcludedFolders();

            // Import only after pruning, so the excluded folders never enter the database
            AssetDatabase.Refresh();

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
    }

    private static void PruneExcludedFolders()
    {
        var target = Path.GetFullPath(TempEmbeddedPath);

        foreach (var folderName in ExcludedFromExport)
        {
            var folder = Path.Combine(target, folderName);

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
                Debug.Log($"[CI] Excluded from export: {folderName}/");
            }

            // The folder's own .meta sits next to it and would export as a stray asset
            var folderMeta = folder + ".meta";

            if (File.Exists(folderMeta))
                File.Delete(folderMeta);
        }
    }

    private static string ReadVersionFromPackageJson(string packagePath)
    {
        var packageJsonPath = Path.Combine(packagePath, "package.json");

        if (!File.Exists(packageJsonPath))
            throw new FileNotFoundException("package.json not found", packageJsonPath);

        var json = File.ReadAllText(packageJsonPath);
        var match = Regex.Match(json, @"""version""\s*:\s*""([^""]+)""");

        if (!match.Success)
            throw new System.Exception("Failed to parse version from package.json");

        return match.Groups[1].Value;
    }

    private static void CleanupTemp()
    {
        var fullTempPath = Path.GetFullPath(TempEmbeddedPath);
        var metaPath = fullTempPath + ".meta"; // path to Nyamu.meta

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
