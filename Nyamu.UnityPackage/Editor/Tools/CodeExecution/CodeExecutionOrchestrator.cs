using System;
using System.Collections.Generic;
using Nyamu.Core.Interfaces;
using UnityEditor.Compilation;

namespace Nyamu.Tools.CodeExecution
{
    // Drives a single execution's record through queued -> compiling -> compiled -> executing ->
    // completed|failed. Start() must be called from the Unity main thread (via
    // context.UnityExecutor.Enqueue), matching AssemblyBuilder's own main-thread requirement.
    internal static class CodeExecutionOrchestrator
    {
        public static void Start(CodeExecutionRecord record, IExecutionContext context)
        {
            record.Phase = "compiling";

            var started = CodeCompilerService.StartBuild(record, context.CompilationState.IsCompiling,
                (rec, messages) => OnBuildFinished(rec, messages, context), out var error, out var outcome);
            if (!started)
                record.Fail(outcome, error);
        }

        static void OnBuildFinished(CodeExecutionRecord record, CompilerMessage[] messages, IExecutionContext context)
        {
            record.CompileSeconds = (DateTime.UtcNow - record.StartedUtc).TotalSeconds;

            var errors = new List<CodeCompileMessage>();
            var warnings = new List<CodeCompileMessage>();
            foreach (var m in messages)
            {
                var mapped = MapMessage(record, m);
                if (m.type == CompilerMessageType.Error)
                    errors.Add(mapped);
                else
                    warnings.Add(mapped);
            }
            record.SetWarnings(warnings);

            if (errors.Count > 0)
            {
                // If the mode was auto-detected, retry once with the other wrapper shape before
                // giving up - a bare expression and a statement block are easy to confuse and the
                // agent shouldn't burn a whole turn on a guess. Deferred to the next tick for the
                // same reason as the invoke below: buildFinished can be raised from inside Unity's
                // own loop over its assembly builders (AssemblyBuilder.status completes the build
                // while EditorCompilation.IsAnyAssemblyBuilderCompiling enumerates), and starting
                // a build there registers a new builder mid-enumeration.
                if (!record.FallbackUsed &&
                    string.Equals(record.Request.mode, "auto", StringComparison.OrdinalIgnoreCase) &&
                    (record.ResolvedMode == "expression" || record.ResolvedMode == "statements"))
                {
                    context.UnityExecutor.Enqueue(() => RetryWithFallback(record, context));
                    return;
                }

                record.SetErrors(errors);
                CodeCompilerService.CleanupTempFiles(record);
                record.Fail("compile_error", $"Compilation failed with {errors.Count} error(s).");
                return;
            }

            record.Phase = "compiled";

            // Dispatch the invoke on the NEXT main-thread tick, deliberately outside
            // buildFinished's own call stack, so user code that touches the compilation
            // pipeline (AssetDatabase.Refresh(), RequestScriptCompilation()) does not re-enter it.
            // Temp files (source + dll + pdb) are cleaned up by CodeRunner after it reads them.
            context.UnityExecutor.Enqueue(() => CodeRunner.RunAndComplete(record, context));
        }

        static void RetryWithFallback(CodeExecutionRecord record, IExecutionContext context)
        {
            CodeCompilerService.CleanupTempFiles(record);

            var fallbackSource = CodeSnippetBuilder.BuildFallback(record.Request, record.ResolvedMode, out var fallbackMode, out var prologueLineCount);
            record.ApplyFallback(fallbackSource, fallbackMode, prologueLineCount);

            var started = CodeCompilerService.StartBuild(record, context.CompilationState.IsCompiling,
                (rec, messages) => OnBuildFinished(rec, messages, context), out var error, out var outcome);
            if (!started)
                record.Fail(outcome, error);
        }

        static CodeCompileMessage MapMessage(CodeExecutionRecord record, CompilerMessage m)
        {
            var file = m.file ?? "";
            var line = m.line;

            if (!file.EndsWith(record.VirtualFileName, StringComparison.OrdinalIgnoreCase))
            {
                // #line was not honoured for this diagnostic - fall back to subtracting the
                // wrapper's known prologue length from the physical line in the generated file.
                line = Math.Max(1, m.line - record.PrologueLineCount);
            }

            return new CodeCompileMessage
            {
                file = record.VirtualFileName,
                line = line,
                column = m.column,
                severity = m.type == CompilerMessageType.Error ? "error" : "warning",
                message = JsonSafe.Sanitize(m.message)
            };
        }
    }
}
