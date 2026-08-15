using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using UnityEditor;
using UnityEngine;

namespace Nyamu.Tools.CodeExecution
{
    // Loads the compiled assembly, resolves the entry point, invokes it with log/stdout capture,
    // and drives Task completion. Every public entry point here runs on the Unity main thread
    // (dispatched via UnityExecutor.Enqueue by CodeExecutionOrchestrator); InvokeOnWorkerThread is
    // the one exception, hopping to a ThreadPool thread for the invoke itself.
    internal static class CodeRunner
    {
        public static void RunAndComplete(CodeExecutionRecord record, IExecutionContext context)
        {
            byte[] dllBytes;
            byte[] pdbBytes = null;
            try
            {
                dllBytes = File.ReadAllBytes(record.DllPath);
                if (!string.IsNullOrEmpty(record.PdbPath) && File.Exists(record.PdbPath))
                    pdbBytes = File.ReadAllBytes(record.PdbPath);
            }
            catch (Exception ex)
            {
                record.Fail("internal_error", $"Failed to read compiled assembly: {ex.Message}");
                return;
            }
            finally
            {
                CodeCompilerService.CleanupTempFiles(record);
            }

            Assembly assembly;
            try
            {
                assembly = pdbBytes != null ? Assembly.Load(dllBytes, pdbBytes) : Assembly.Load(dllBytes);
                context.CodeExecutionState.IncrementAssembliesLoaded();
            }
            catch (Exception ex)
            {
                record.Fail("internal_error", $"Failed to load compiled assembly: {ex.Message}");
                return;
            }

            MethodInfo entry;
            List<string> candidateNames;
            var entryPointName = string.IsNullOrEmpty(record.Request.entryPoint) ? "Execute" : record.Request.entryPoint;
            try
            {
                entry = ResolveEntryPoint(assembly, record.ResolvedMode, entryPointName, out candidateNames);
            }
            catch (Exception ex)
            {
                record.Fail("internal_error", $"Failed to resolve entry point: {ex.Message}");
                return;
            }

            if (entry == null)
            {
                var hint = candidateNames.Count == 0
                    ? $"No public static parameterless method named '{entryPointName}' was found."
                    : $"Multiple candidates found: {string.Join(", ", candidateNames)}. Set entry_point to disambiguate.";
                record.Fail("no_entry_point", hint);
                return;
            }

            record.Phase = "executing";
            record.AssemblyName = assembly.GetName().Name;

            if (record.Request.runOnMainThread)
                InvokeOnMainThread(record, entry);
            else
                InvokeOnWorkerThread(record, context, entry);
        }

        static MethodInfo ResolveEntryPoint(Assembly assembly, string resolvedMode, string entryPointName, out List<string> candidateNames)
        {
            candidateNames = new List<string>();

            if (resolvedMode != "class")
                return assembly.GetType("NyamuGenerated.Entry")?.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);

            MethodInfo found = null;
            foreach (var type in assembly.GetTypes())
            {
                var method = type.GetMethod(entryPointName,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (method == null)
                    continue;

                candidateNames.Add($"{type.FullName}.{method.Name}");
                found = method;
            }

            return candidateNames.Count == 1 ? found : null;
        }

        static void InvokeOnMainThread(CodeExecutionRecord record, MethodInfo entry)
        {
            var stopwatch = Stopwatch.StartNew();
            object raw;

            using (BeginCapture(record))
            {
                try
                {
                    raw = entry.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                    record.ExecuteSeconds = stopwatch.Elapsed.TotalSeconds;
                    record.SetException(inner.GetType().FullName, JsonSafe.Sanitize(inner.Message), TrimStackTrace(inner.ToString()));
                    record.Fail("runtime_exception", inner.Message);
                    return;
                }
            }

            record.ExecuteSeconds = stopwatch.Elapsed.TotalSeconds;
            CompleteWithValue(record, raw);
        }

        static void InvokeOnWorkerThread(CodeExecutionRecord record, IExecutionContext context, MethodInfo entry)
        {
            var stopwatch = Stopwatch.StartNew();
            var budgetMs = record.Request.timeout > 0 ? record.Request.timeout * 1000 : 60000;

            // Application.logMessageReceivedThreaded's add/remove accessors call into Unity's
            // native SetLogCallbackDefined, which throws UnityException off the main thread -
            // "Threaded" only describes thread-safe delivery of an already-registered callback,
            // not subscribing/unsubscribing to it. So capture must start and end here, on the
            // main thread (InvokeOnWorkerThread itself runs via UnityExecutor.Enqueue); only the
            // entry.Invoke call itself runs on the worker thread.
            var capture = BeginCapture(record);

            // Reflection reaching into certain UnityEditor types can itself deadlock waiting on
            // the main thread even though run_on_main_thread=false promises a non-blocking call.
            // There is no safe way to abort a running .NET thread (Thread.Abort is not usable
            // under Unity's Mono), so this watchdog cannot stop stuck work - it only stops
            // code_execute from being wedged forever by settling the record (and releasing the
            // "one execution at a time" gate) on a timeout, regardless of whether the worker
            // thread ever comes back.
            var settled = 0;

            // A raw background Thread, not Task.Run/ThreadPool: a ThreadPool-queued work item was
            // observed to never start at all in this Unity Editor process, while a dedicated
            // Thread runs immediately. Root cause not fully diagnosed - suspected ThreadPool
            // starvation/scheduling interaction with Unity's own embedding of Mono - but this
            // sidesteps it.
            var thread = new Thread(() =>
            {
                object raw = null;
                Exception thrown = null;

                try
                {
                    raw = entry.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    thrown = (ex as TargetInvocationException)?.InnerException ?? ex;
                }

                if (Interlocked.CompareExchange(ref settled, 1, 0) != 0)
                    return; // the watchdog already timed this execution out; drop the late result

                // Hop back to the main thread: formatting UnityEngine.Object results (and any
                // further Unity API access, including ending the capture) is only safe there,
                // and record mutation stays single-writer alongside the rest of the pipeline.
                context.UnityExecutor.Enqueue(() =>
                {
                    capture.Dispose();
                    record.ExecuteSeconds = stopwatch.Elapsed.TotalSeconds;
                    if (thrown != null)
                    {
                        record.SetException(thrown.GetType().FullName, JsonSafe.Sanitize(thrown.Message), TrimStackTrace(thrown.ToString()));
                        record.Fail("runtime_exception", thrown.Message);
                        return;
                    }
                    CompleteWithValue(record, raw);
                });
            })
            {
                IsBackground = true,
                Name = "NyamuCodeExecuteWorker"
            };
            thread.Start();

            Task.Delay(budgetMs).ContinueWith(_ =>
            {
                if (Interlocked.CompareExchange(ref settled, 1, 0) != 0)
                    return;

                context.UnityExecutor.Enqueue(() =>
                {
                    capture.Dispose();
                    record.ExecuteSeconds = stopwatch.Elapsed.TotalSeconds;
                    record.Fail("worker_thread_timeout",
                        $"The snippet did not return within {budgetMs}ms on the worker thread. It may still be " +
                        "running in the background - there is no safe way to cancel it. This can happen when code " +
                        "touches certain UnityEditor types even with run_on_main_thread=false; retry with " +
                        "run_on_main_thread=true if the snippet needs the Editor API.");
                });
            });
        }

        static void CompleteWithValue(CodeExecutionRecord record, object raw)
        {
            if (raw is IEnumerator)
            {
                record.Fail("unsupported_return",
                    "The snippet returned an IEnumerator (coroutine). code_execute does not drive " +
                    "coroutines in this version - return a value, void, or a Task instead.");
                return;
            }

            if (raw is Task task)
            {
                ParkTask(record, task);
                return;
            }

            FormatAndComplete(record, raw);
        }

        static void ParkTask(CodeExecutionRecord record, Task task)
        {
            var budgetMs = record.Request.asyncBudgetMs > 0 ? record.Request.asyncBudgetMs : 30000;
            var deadlineUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(1000, budgetMs));

            void Tick()
            {
                if (task.IsCompleted)
                {
                    EditorApplication.update -= Tick;
                    AssemblyReloadEvents.beforeAssemblyReload -= Unsubscribe;
                    CompleteTask(record, task);
                    return;
                }

                if (DateTime.UtcNow >= deadlineUtc)
                {
                    EditorApplication.update -= Tick;
                    AssemblyReloadEvents.beforeAssemblyReload -= Unsubscribe;
                    record.Fail("async_timeout", $"The returned Task did not complete within {budgetMs}ms.");
                }
            }

            void Unsubscribe()
            {
                EditorApplication.update -= Tick;
            }

            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Unsubscribe;
        }

        static void CompleteTask(CodeExecutionRecord record, Task task)
        {
            if (task.IsFaulted)
            {
                var inner = task.Exception?.InnerException ?? task.Exception as Exception ?? new Exception("Task faulted.");
                record.SetException(inner.GetType().FullName, JsonSafe.Sanitize(inner.Message), TrimStackTrace(inner.ToString()));
                record.Fail("runtime_exception", inner.Message);
                return;
            }

            if (task.IsCanceled)
            {
                record.Fail("async_timeout", "The returned Task was cancelled before completing.");
                return;
            }

            object value = null;
            var resultProperty = task.GetType().GetProperty("Result");
            if (resultProperty != null)
            {
                try
                {
                    value = resultProperty.GetValue(task);
                }
                catch (Exception ex)
                {
                    record.Fail("internal_error", $"Failed to read Task result: {ex.Message}");
                    return;
                }
            }

            FormatAndComplete(record, value);
        }

        static void FormatAndComplete(CodeExecutionRecord record, object raw)
        {
            try
            {
                var (text, typeName) = ResultFormatter.Describe(raw, record.Request.maxResultChars);
                record.SetResult(typeName, text);
                record.Complete("success", "Execution completed.");
            }
            catch (Exception ex)
            {
                record.Fail("internal_error", $"Execution succeeded but result formatting failed: {ex.Message}");
            }
        }

        static string TrimStackTrace(string fullTrace)
        {
            if (string.IsNullOrEmpty(fullTrace))
                return "";

            var lines = fullTrace.Replace("\r\n", "\n").Split('\n');
            var kept = new List<string>();
            foreach (var line in lines)
            {
                if (line.Contains("System.Reflection.") || line.Contains("RuntimeMethodInfo.Invoke") ||
                    line.Contains("Nyamu.Tools.CodeExecution."))
                    break;
                kept.Add(line.Replace("NyamuGenerated.Entry.Execute", "<user code>"));
            }

            return JsonSafe.Sanitize(string.Join("\n", kept).TrimEnd());
        }

        static IDisposable BeginCapture(CodeExecutionRecord record) => new CaptureScope(record);

        sealed class CaptureScope : IDisposable
        {
            readonly CodeExecutionRecord _record;
            readonly TextWriter _prevOut;
            readonly TextWriter _prevErr;
            readonly CappedStringWriter _writer;
            readonly Application.LogCallback _handler;

            public CaptureScope(CodeExecutionRecord record)
            {
                _record = record;
                _prevOut = Console.Out;
                _prevErr = Console.Error;
                _writer = new CappedStringWriter(8000);
                Console.SetOut(_writer);
                Console.SetError(_writer);

                var maxLogEntries = record.Request.maxLogEntries > 0 ? record.Request.maxLogEntries : 200;
                _handler = (condition, trace, type) =>
                    _record.AddLog(JsonSafe.Sanitize($"[{type}] {condition}"), maxLogEntries);
                Application.logMessageReceivedThreaded += _handler;
            }

            public void Dispose()
            {
                Application.logMessageReceivedThreaded -= _handler;
                Console.SetOut(_prevOut);
                Console.SetError(_prevErr);
                _record.SetStdout(JsonSafe.Sanitize(_writer.ToString()));
            }
        }

        sealed class CappedStringWriter : TextWriter
        {
            readonly StringBuilder _sb = new();
            readonly int _limit;
            bool _truncated;

            public CappedStringWriter(int limit)
            {
                _limit = limit;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                if (_truncated)
                    return;
                if (_sb.Length >= _limit)
                {
                    _truncated = true;
                    _sb.Append(" …(truncated)");
                    return;
                }
                _sb.Append(value);
            }

            public override void Write(string value)
            {
                if (_truncated || string.IsNullOrEmpty(value))
                    return;

                var remaining = _limit - _sb.Length;
                if (remaining <= 0)
                {
                    _truncated = true;
                    _sb.Append(" …(truncated)");
                    return;
                }

                if (value.Length > remaining)
                {
                    _sb.Append(value.Substring(0, remaining));
                    _truncated = true;
                    _sb.Append(" …(truncated)");
                }
                else
                {
                    _sb.Append(value);
                }
            }

            public override string ToString() => _sb.ToString();
        }
    }

    // JsonUtility.ToJson does not escape control characters below 0x20 other than \n\r\t, so
    // anything captured from user code (logs, exception text, compiler messages) must be
    // sanitized before it reaches a [Serializable] DTO or the HTTP response body becomes invalid JSON.
    internal static class JsonSafe
    {
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? "";

            StringBuilder sb = null;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c < 0x20 && c != '\n' && c != '\r' && c != '\t')
                {
                    sb ??= new StringBuilder(value.Substring(0, i));
                    sb.Append("\\u").Append(((int)c).ToString("x4"));
                }
                else
                {
                    sb?.Append(c);
                }
            }
            return sb?.ToString() ?? value;
        }
    }
}
