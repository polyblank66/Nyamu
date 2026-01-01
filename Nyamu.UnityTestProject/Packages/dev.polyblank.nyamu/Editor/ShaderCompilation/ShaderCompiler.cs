using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Nyamu.Tools.Shaders;

namespace Nyamu.ShaderCompilation
{
    // Low-level shader compilation utilities
    public static class ShaderCompiler
    {
        // Async version using TaskCompletionSource
        public static async Task<ShaderCompileResult> CompileShaderAtPathAsync(string shaderPath)
        {
            var startTime = DateTime.Now;

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                return new ShaderCompileResult
                {
                    shaderName = Path.GetFileNameWithoutExtension(shaderPath),
                    shaderPath = shaderPath,
                    hasErrors = true,
                    errorCount = 1,
                    errors = new[] { new ShaderCompileError { message = "Failed to load shader asset", file = shaderPath } }
                };
            }

            ShaderUtil.ClearShaderMessages(shader);
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            // Async wait for compilation using TaskCompletionSource
            var timeout = DateTime.Now.AddSeconds(10);
            var tcs = new TaskCompletionSource<bool>();

            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if (!ShaderUtil.anythingCompiling || DateTime.Now >= timeout)
                {
                    EditorApplication.update -= callback;
                    tcs.TrySetResult(true);
                }
            };

            EditorApplication.update += callback;
            await tcs.Task;

            var compilationTime = (DateTime.Now - startTime).TotalSeconds;
            shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);

            var messageCount = ShaderUtil.GetShaderMessageCount(shader);
            var errors = new List<ShaderCompileError>();
            var warnings = new List<ShaderCompileError>();

            for (var i = 0; i < messageCount; i++)
            {
                var msg = ShaderUtil.GetShaderMessages(shader)[i];
                var error = new ShaderCompileError
                {
                    message = msg.message,
                    messageDetails = msg.messageDetails,
                    file = msg.file,
                    line = msg.line,
                    platform = msg.platform.ToString()
                };

                if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    errors.Add(error);
                else if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)
                    warnings.Add(error);
            }

            var platformNames = new List<string> { "Unknown" };

            return new ShaderCompileResult
            {
                shaderName = shader.name,
                shaderPath = shaderPath,
                hasErrors = errors.Count > 0,
                hasWarnings = warnings.Count > 0,
                errorCount = errors.Count,
                warningCount = warnings.Count,
                errors = errors.ToArray(),
                warnings = warnings.ToArray(),
                compilationTime = compilationTime,
                targetPlatforms = platformNames.ToArray()
            };
        }

        // Synchronous version using polling (no deadlock on main thread)
        public static ShaderCompileResult CompileShaderAtPath(string shaderPath)
        {
            var startTime = DateTime.Now;

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                return new ShaderCompileResult
                {
                    shaderName = Path.GetFileNameWithoutExtension(shaderPath),
                    shaderPath = shaderPath,
                    hasErrors = true,
                    errorCount = 1,
                    errors = new[] { new ShaderCompileError { message = "Failed to load shader asset", file = shaderPath } }
                };
            }

            ShaderUtil.ClearShaderMessages(shader);
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            // Polling approach to avoid deadlock on main thread
            var timeout = DateTime.Now.AddSeconds(10);
            while (ShaderUtil.anythingCompiling && DateTime.Now < timeout)
                Thread.Sleep(50);

            var compilationTime = (DateTime.Now - startTime).TotalSeconds;
            shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);

            var messageCount = ShaderUtil.GetShaderMessageCount(shader);
            var errors = new List<ShaderCompileError>();
            var warnings = new List<ShaderCompileError>();

            for (var i = 0; i < messageCount; i++)
            {
                var msg = ShaderUtil.GetShaderMessages(shader)[i];
                var error = new ShaderCompileError
                {
                    message = msg.message,
                    messageDetails = msg.messageDetails,
                    file = msg.file,
                    line = msg.line,
                    platform = msg.platform.ToString()
                };

                if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    errors.Add(error);
                else if (msg.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)
                    warnings.Add(error);
            }

            var platformNames = new List<string> { "Unknown" };

            return new ShaderCompileResult
            {
                shaderName = shader.name,
                shaderPath = shaderPath,
                hasErrors = errors.Count > 0,
                hasWarnings = warnings.Count > 0,
                errorCount = errors.Count,
                warningCount = warnings.Count,
                errors = errors.ToArray(),
                warnings = warnings.ToArray(),
                compilationTime = compilationTime,
                targetPlatforms = platformNames.ToArray()
            };
        }
    }
}
