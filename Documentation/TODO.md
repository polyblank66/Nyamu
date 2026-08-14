# TODO

Findings that turned up while investigating the codebase but were out of scope
for the change in progress at the time. Recorded here so they aren't lost.

## From: Play Mode observability and control over MCP (2026-08-14)

1. **`UnityThreadExecutor.Process()` invokes queued actions while holding the
   queue lock** (`Nyamu.UnityPackage/Editor/Core/UnityThreadExecutor.cs:21-28`).
   Any action that transitively calls `Enqueue` self-deadlocks the Unity main
   thread, and a slow action blocks every HTTP handler thread trying to
   enqueue (handlers run on ThreadPool threads,
   `Nyamu.UnityPackage/Editor/Http/TcpHttpServer.cs:114`). Fix: drain the
   queue into a local list under the lock, then invoke outside it. While
   there, consider wrapping each invocation in try/catch so one throwing
   action doesn't abort the rest of the batch (today an exception escapes
   into `EditorApplication.update`). Nothing triggers this today - it's a
   trap for future tools. The new `PlayModeSwitch.Request` helper
   (`Nyamu.UnityPackage/Editor/Tools/Editor/PlayMode/PlayModeSwitch.cs`)
   deliberately never calls `Enqueue` from within its own enqueued action, and
   carries a comment explaining why.

2. **Most `call*` handlers in `mcp-server.js` destroy typed Unity errors** by
   wrapping them in `throw new Error(...)`, so the `instanceof
   UnityUnavailableError || UnityRestartingError` check in `handleToolCall`
   fails and the agent loses `data.instructions` / `data.retryable`. A
   `rethrowUnityError(error, context)` helper was added and applied to
   `callEditorStatus`, `callEditorExitPlayMode`, and `callEditorEnterPlayMode`
   only. The remaining ~17 call sites (compile, test, shader, menu-item,
   asset-refresh handlers) have the same defect and should get the same
   treatment.

3. **`Server.Cleanup()` unsubscribes the queue drainer before stopping the
   HTTP server** (`Nyamu.UnityPackage/Editor/NyamuServer.cs`:
   `_editorMonitor?.Cleanup()` runs before `_httpServer?.Stop()`), so once a
   domain reload begins the `UnityThreadExecutor` queue is guaranteed never
   to drain again. The Play Mode tools work around this with a bounded
   3-second wait (`PlayModeSwitch.MainThreadDeadlineMs`) rather than blocking
   forever, but reversing the cleanup order would fix the root cause and
   remove the need for that workaround (and for any other tool that enqueues
   main-thread work and waits on it).

4. **Stale endpoint names in `NyamuServer-API-Guide.md`**: `/compilation-trigger`
   and `/compilation-status` (now `/scripts-compile`, `/scripts-compile-status`),
   and `/tests-status` / `/tests-cancel` (now `/tests-run-status`,
   `/tests-run-cancel`) - see the "Usage Examples" and "Test Status" / "Cancel
   Tests" sections. The same dead `/compilation-status` URL appeared in
   `mcp-server.js`'s `ECONNREFUSED` error instructions and was corrected in
   passing; the guide's own examples were left alone to keep that change
   scoped.

5. **`menu_items_execute` is absent from the Postman collection**, and it is
   documented as `POST` with a `menu_item_path` body parameter in
   `NyamuServer-API-Guide.md` while the implementation reads a `GET` query
   string parameter `menuItemPath`
   (`Nyamu.UnityPackage/Editor/NyamuServer.cs`, `HandleExecuteMenuItemRequest`).
   Docs and implementation disagree; pick one and fix the other.

6. **`ExecuteMenuItemTool` reports a misleading message on timeout** - its
   bounded 1-second poll
   (`Nyamu.UnityPackage/Editor/Tools/Editor/ExecuteMenuItemTool.cs:45-48`)
   falls through to `"MenuItem execution failed"`, conflating "menu item not
   found" with "main thread did not respond in time". The
   `PlayModeSwitchOutcome` enum pattern in
   `Nyamu.UnityPackage/Editor/Tools/Editor/PlayMode/PlayModeSwitch.cs` is a
   reasonable shape to copy: distinct outcomes for not-found vs. blocked vs.
   timed-out vs. errored, each with its own honest message.
