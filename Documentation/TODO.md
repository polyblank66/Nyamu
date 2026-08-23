# TODO

Findings that turned up while investigating the codebase but were out of scope
for the change in progress at the time. Recorded here so they aren't lost.

## From: Execute Code tool (2026-08-15)

1. **`Task.Run`-queued work was observed to never start inside this Unity
   Editor process.** While implementing `code_execute`'s `run_on_main_thread:
   false` path, a `Task.Run(...)` closure containing nothing but a trivial
   `entry.Invoke(null, null)` call on a freshly loaded assembly never began
   executing - not even the first line - until a separate watchdog
   (`Task.Delay(...).ContinueWith(...)`) timed it out. `Task.Delay` and
   `TcpHttpServer`'s own per-connection `Task.Run` calls
   (`Nyamu.UnityPackage/Editor/Http/TcpHttpServer.cs:114`) both work fine, so
   the ThreadPool isn't universally broken in this environment - something
   about scheduling a fresh work item at that particular moment (main-thread
   dispatch mid-`EditorApplication.update`, freshly `Assembly.Load`ed code, or
   some interaction between the two) starves it. Root cause not fully
   diagnosed. Worked around in
   `Nyamu.UnityPackage/Editor/Tools/CodeExecution/CodeRunner.cs`
   (`InvokeOnWorkerThread`) by using a dedicated background `Thread` instead
   of `Task.Run`, which was observed to start immediately every time. Worth a
   proper investigation before any other feature reaches for `Task.Run` from
   Editor-side Nyamu code.

2. **`Application.logMessageReceivedThreaded`'s add/remove accessors are not
   actually safe to call off the main thread**, despite the name: subscribing
   (`+=`) from a background thread throws `UnityException: SetLogCallbackDefined
   can only be called from the main thread`. "Threaded" only describes
   thread-safe *delivery* of an already-registered callback. This was caught
   because it silently killed the `code_execute` worker thread before
   `entry.Invoke` ever ran (an unhandled exception on a raw `Thread` just
   ends that thread) - which looked identical to the `Task.Run` starvation
   issue above (item 1) until logged output proved otherwise. Fixed in
   `Nyamu.UnityPackage/Editor/Tools/CodeExecution/CodeRunner.cs` by moving
   capture start/stop (the `Application.logMessageReceivedThreaded`
   subscription and `Console.SetOut`/`SetError`) onto the main thread, leaving
   only the `entry.Invoke` call itself on the worker thread. Worth checking
   whether any other Nyamu code subscribes to Unity log/console callbacks
   from a non-main thread.

## From: Play Mode focus stall (2026-08-23)

1. **`code_execute` rejected two consecutive calls with `editor_busy` /
   "Unity is compiling." while `editor_status` reported `isCompiling: false`
   in between.** The gate is
   `Nyamu.UnityPackage/Editor/Tools/CodeExecution/CodeCompilerService.cs:20`,
   which reads `EditorApplication.isCompiling`. That flag is not the same
   signal `CompilationStateManager` tracks off the `CompilationPipeline`
   events: Unity also raises it while any `AssemblyBuilder` is running,
   including the one `code_execute` itself uses. A builder left registered by
   a previous execution therefore locks out every subsequent call, and the two
   status sources disagree in a way no caller can act on - the agent is told
   to wait for a compilation that `editor_status` says is not happening.
   Worth checking against the fix in 3a1fb40 ("defer code_execute fallback
   retry off buildFinished"), which touched exactly this lifecycle; it may be
   an unfinished tail of that. At minimum the two should report the same
   thing, or the rejection message should name the real reason.

2. **A playing, unfocused Editor with "Run In Background" off stops serving
   the Nyamu HTTP endpoint entirely - not just the main-thread tools.** The
   expected failure was partial: everything touching a Unity API is queued
   onto `UnityThreadExecutor` and drained only from `EditorApplication.update`
   (`Nyamu.UnityPackage/Editor/Core/Monitors/EditorMonitor.cs:37`), so
   suspending that loop should strand the queue while `TcpHttpServer` keeps
   answering - it serves each connection on the thread pool
   (`Nyamu.UnityPackage/Editor/Http/TcpHttpServer.cs:113`) and the status tools
   read nothing but cached state. Observed behaviour is that `editor_status`
   goes unanswered too, so something suspends more than the Editor loop.
   Candidates, none verified: thread pool work items not being scheduled (see
   item 1 of "From: Execute Code tool" above, which reports exactly that
   symptom in this process); a Unity
   native call made from the HTTP thread blocking on the suspended main thread
   - `RouteRequest` handlers reach `JsonUtility.ToJson`
   (`Nyamu.UnityPackage/Editor/NyamuServer.cs:536`) and `NyamuLogger` reaches
   `UnityEngine.Debug`, neither of which is documented as thread-safe; or a GC
   that cannot stop the world because the main thread never reaches a
   safepoint. A background thread writing timestamps to a file with a raw
   `FileStream`, paired with a second thread that allocates, would separate
   those three. Worth settling because it decides where recovery can live: if
   the process genuinely cannot reply, a server-side "honest timeout" response
   is impossible and the diagnosis has to be produced client-side in
   `mcp-server.js` when the connection times out.
