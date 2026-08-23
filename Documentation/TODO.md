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

1. **The HTTP accept loop now depends on the ThreadPool starting a `Task.Run`
   work item - and item 1 of "From: Execute Code tool" above records that
   failing once in this very process.** `TcpHttpServer.Start()` launches the
   loop with `Task.Run(() => AcceptLoopAsync(_cts.Token))`
   (`Nyamu.UnityPackage/Editor/Http/TcpHttpServer.cs:50`) to keep it off the
   Unity main thread: awaiting on `UnitySynchronizationContext` posted the
   loop's continuations back to the main thread, so a playing, unfocused Editor
   with "Run In Background" off stopped serving every endpoint. The trade is
   that the accept loop is now the single point of failure for the whole
   endpoint - if that work item never starts, the port binds successfully and
   nothing is ever accepted, so clients hang on connections the kernel completed
   into the backlog. Silent total failure, with no error logged because the bind
   itself succeeded. Not an observed fault: per-connection dispatch already used
   `Task.Run`, and the pool behaved in every measured run. Removing the
   dependency outright means a dedicated background `Thread` running a
   synchronous `AcceptTcpClient()` loop, unblocked by `listener.Stop()` - the
   same workaround `CodeRunner.InvokeOnWorkerThread` already uses, for the same
   reason.

## From: Integration test data rename (2026-08-24)

1. **`tests_run_*` cannot filter by assembly or category, so no MCP call runs
   only the package's real tests.** `TestExecutionService` populates Unity's
   `Filter` with `testNames` and `groupNames` only
   (`Nyamu.UnityPackage/Editor/TestExecution/TestExecutionService.cs:129`),
   leaving `Filter.assemblyNames` and `Filter.categoryNames` unused. The
   repository holds two disjoint sets of tests: `Nyamu.EditorTests`, which must
   always be green, and `Nyamu.IntegrationTestData.*`, of which three EditMode
   and four PlayMode members fail by design because they are the subjects the
   Python tests in `IntegrationTests/` exercise the `tests_run_*` tools against.
   `tests_run_all` therefore mixes the two and never comes back green, which
   makes it useless as a health check. Unity's `Filter` has no exclusion
   mechanism - "everything except the test data" can only be expressed as an
   assembly or category whitelist - so the fix is to expose `assembly_names`
   and `category_names` as parameters on `tests_run_all` and `tests_run_regex`.
   The rename to `Nyamu.IntegrationTestData` and the
   `[Category("IntegrationTestData")]` attributes were done in preparation.
   Until the parameters exist the workaround is
   `tests_run_regex(test_filter_regex="Nyamu\.EditorTests\..*")`.
