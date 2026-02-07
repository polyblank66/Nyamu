#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.Burst;
using UnityEditor;
using UnityEngine;

static class BurstValidator
{
    // Match patterns like: path(line,col): Burst error/warning
    static readonly Regex BurstErrorRx = new(@"\(\d+,\d+\):\s*Burst error", RegexOptions.Multiline);
    static readonly Regex BurstWarningRx = new(@"\(\d+,\d+\):\s*Burst warning", RegexOptions.Multiline);

    [MenuItem("Tools/Validate Burst")]
    public static void ValidateAllBurstCode()
    {
        Debug.Log("=== Burst validation started ===");

        var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        var burstReflectionType = allAssemblies
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
            .FirstOrDefault(t => t.FullName == "Unity.Burst.Editor.BurstReflection");
        var burstCompilerServiceType = allAssemblies
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
            .FirstOrDefault(t => t.FullName == "Unity.Burst.LowLevel.BurstCompilerService");

        if (burstReflectionType == null || burstCompilerServiceType == null)
        {
            Debug.LogError("Could not find Burst internal types");
            return;
        }

        var assembliesField = burstReflectionType.GetField("EditorAssembliesThatCanPossiblyContainJobs",
            BindingFlags.Static | BindingFlags.Public);
        var assemblies = assembliesField?.GetValue(null);
        if (assemblies == null)
        {
            Debug.LogError("Could not get EditorAssembliesThatCanPossiblyContainJobs");
            return;
        }

        var findMethod = burstReflectionType.GetMethod("FindExecuteMethods",
            BindingFlags.Static | BindingFlags.Public);
        if (findMethod == null)
        {
            Debug.LogError("Could not find BurstReflection.FindExecuteMethods");
            return;
        }

        var result = findMethod.Invoke(null, new object[] { assemblies, 0 });
        var targets = result.GetType().GetField("CompileTargets")?.GetValue(result) as System.Collections.IList;

        if (targets == null || targets.Count == 0)
        {
            Debug.Log("=== No Burst compile targets found ===");
            return;
        }

        var getDisassembly = burstCompilerServiceType.GetMethod("GetDisassembly",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (getDisassembly == null)
        {
            Debug.LogError("Could not find BurstCompilerService.GetDisassembly");
            return;
        }

        var optionsInstance = BurstCompiler.Options;
        var tryGetOptionsMethod = optionsInstance.GetType().GetMethod("TryGetOptions",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        var errors = 0;
        var warnings = 0;
        var compiled = 0;

        foreach (var target in targets)
        {
            var targetType = target.GetType();
            var method = targetType.GetField("Method")?.GetValue(target) as MethodInfo;

            if (method == null) continue;

            var isStatic = (bool)(targetType.GetField("IsStaticMethod")?.GetValue(target) ?? false);
            var jobType = targetType.GetField("JobType")?.GetValue(target) as Type;
            var memberInfo = isStatic ? (MemberInfo)method : jobType;
            var displayName = memberInfo?.ToString() ?? method.Name;

            var options = "";
            if (tryGetOptionsMethod != null && memberInfo != null)
            {
                var args = new object[] { memberInfo, null, false, true, false };
                var success = (bool)tryGetOptionsMethod.Invoke(optionsInstance, args);
                if (success)
                    options = (args[1] as string ?? "") + "\n--target=Auto\n--dump=Asm";
            }

            try
            {
                var disassembly = getDisassembly.Invoke(null, new object[] { method, options }) as string ?? "";

                if (BurstErrorRx.IsMatch(disassembly))
                {
                    errors++;
                    Debug.LogError($"[Burst ERROR] {displayName}\n{disassembly}");
                }
                else if (BurstWarningRx.IsMatch(disassembly))
                {
                    warnings++;
                    Debug.LogWarning($"[Burst WARNING] {displayName}\n{disassembly}");
                }
                else
                    compiled++;
            }
            catch (Exception e)
            {
                errors++;
                Debug.LogError($"[Burst EXCEPTION] {displayName}\n{e}");
            }
        }

        Debug.Log($"=== Burst validation finished. OK: {compiled}, Errors: {errors}, Warnings: {warnings} ===");
    }
}
#endif
