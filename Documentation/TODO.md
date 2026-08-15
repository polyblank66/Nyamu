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
