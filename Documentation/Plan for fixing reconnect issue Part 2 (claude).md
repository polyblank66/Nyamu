# NyamuServer HTTP Threading Modernization Plan

## Context

The NyamuServer HTTP implementation uses a legacy async pattern (APM - Asynchronous Programming Model) with `Thread` + `ManualResetEvent` synchronization. This creates multiple race conditions during cleanup that prevent proper port release and cause intermittent failures during Unity domain reloads.

**Core Problem:** The current pattern cannot gracefully cancel pending HTTP accept operations:
- `BeginGetContext()` has no cancellation mechanism
- Thread blocks on `ManualResetEvent.WaitOne()` indefinitely if callback never fires
- During cleanup, the event can be disposed while callbacks are executing → `ObjectDisposedException`
- The event can be null-checked but then dereferenced → `NullReferenceException`

**Why This Matters:**
- Port reconnection issues during domain reload (see `Documentation/Port reconnect issue.md`)
- "HTTP thread did not stop gracefully" warnings in logs
- Potential for hung threads blocking Unity exit

**Solution:** Modernize to Task-based async pattern (`async/await` + `CancellationTokenSource`) following the recommended pattern in `Documentation/Server with HttpListerner for Unity (chatgpt).md`. This eliminates race conditions by removing the ManualResetEvent entirely and using proper cancellation tokens.

## Implementation Approach

### 1. Replace Threading Primitives

**File:** `C:\code\Nyamu\Nyamu.UnityPackage\Editor\NyamuServer.cs`

**Current fields (lines 98-103):**
```csharp
static HttpListener _listener;
static Thread _thread;
static ManualResetEvent _listenerReady;
static volatile bool _shouldStop;
```

**New fields:**
```csharp
static HttpListener _listener;
static Task _acceptTask;
static CancellationTokenSource _cancellation;
```

**Rationale:**
- `Task` replaces `Thread` - standard async pattern with built-in cancellation support
- `CancellationTokenSource` replaces `volatile bool` - proper async cancellation
- Remove `ManualResetEvent` - no longer needed with `GetContextAsync()`

### 2. Implement Async Request Loop

**Replace:** `HttpRequestProcessor()` (lines 396-411) and `ProcessRequestCallback()` (lines 360-394)

**New method (following ChatGPT pattern lines 101-128):**
```csharp
static async Task AcceptRequestsAsync(CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        try
        {
            // GetContextAsync() is cancellation-aware
            var contextTask = _listener.GetContextAsync();

            // Race between context arrival and cancellation
            var completed = await Task.WhenAny(contextTask, Task.Delay(-1, token));

            if (completed != contextTask)
                break; // Cancellation won the race

            var context = await contextTask;

            // Process in ThreadPool (preserves existing multi-threading behavior)
            _ = Task.Run(() => ProcessHttpRequest(context), token);
        }
        catch (ObjectDisposedException) { break; } // Listener stopped
        catch (HttpListenerException) { break; }  // Listener error
        catch (Exception ex) { HandleHttpException(ex); }
    }
}
```

**Key Benefits:**
- `GetContextAsync()` is properly cancellable (BeginGetContext is not)
- `Task.WhenAny()` allows clean shutdown without abandoning requests
- No callbacks, no ManualResetEvent, no race conditions
- Exceptions properly typed and handled

**Unchanged:**
- `ProcessHttpRequest()` (lines 413-422) - request handler logic unchanged
- `RouteRequest()` (lines 433-456) - endpoint routing unchanged
- All tool handlers - business logic unchanged

### 3. Update Initialize()

**Location:** Lines 219-221

**Current:**
```csharp
_thread = new(HttpRequestProcessor);
_thread.IsBackground = true;
_thread.Start();
```

**New:**
```csharp
_cancellation = new CancellationTokenSource();
_acceptTask = AcceptRequestsAsync(_cancellation.Token);
```

**Defensive Check (add at start of Initialize for domain reload safety):**
```csharp
// Domain reload safety: ensure previous instance is fully cleaned up
// When "Disable Domain Reload" is enabled, static fields persist across Play Mode transitions
if (_acceptTask != null || _cancellation != null || _listener != null)
{
    NyamuLogger.LogDebug("[Nyamu][Server] Detected stale state, forcing cleanup");
    Cleanup();
}
```

**Why:** Per the updated ChatGPT documentation (lines 240-299):
- When domain reload is disabled: static fields NOT reset, threads KEEP RUNNING
- `[InitializeOnLoad]` does NOT fire again on Play Mode transitions
- Must explicitly detect and recover from stale state
- Without this check: zombie state with cancelled tokens and dead listeners

**Self-Healing Pattern (optional enhancement for future):**
```csharp
// Subscribe to play mode changes for proactive state validation
EditorApplication.playModeStateChanged += OnPlayModeChanged;

static void OnPlayModeChanged(PlayModeStateChange state)
{
    if (state == PlayModeStateChange.EnteredEditMode)
    {
        // Ensure server is running after returning from Play Mode
        if (_listener == null || !_listener.IsListening)
        {
            Cleanup(); // Clear stale state
            Initialize(); // Restart server
        }
    }
}
```

**Current Implementation:** Defensive check in `Initialize()` is sufficient for Phase 1. The self-healing pattern can be added later if testing reveals edge cases where the server enters a broken state.

### 4. Rewrite Cleanup()

**Location:** Lines 299-346

**New implementation:**
```csharp
static void Cleanup()
{
    // Event unsubscription (idempotent)
    EditorApplication.quitting -= Cleanup;
    AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;

    // Cleanup monitors
    _compilationMonitor?.Cleanup();
    _editorMonitor?.Cleanup();
    SaveTimestampsCache();

    // 1. Cancel accept loop (idempotent - safe to call multiple times)
    try
    {
        _cancellation?.Cancel();
    }
    catch (ObjectDisposedException) { } // Already disposed

    // 2. Stop listener (releases port immediately)
    if (_listener != null)
    {
        try
        {
            if (_listener.IsListening) _listener.Stop();
            _listener.Close();
        }
        catch { }
        finally { _listener = null; }
    }

    // 3. Wait for accept loop to exit (increased timeout from 1s to 3s)
    if (_acceptTask != null)
    {
        try
        {
            if (!_acceptTask.Wait(3000))
                NyamuLogger.LogWarning("[Nyamu][Server] Accept loop did not stop within 3s");
        }
        catch (AggregateException) { } // Expected from task cancellation
        finally { _acceptTask = null; }
    }

    // 4. Dispose cancellation token
    try
    {
        _cancellation?.Dispose();
        _cancellation = null;
    }
    catch { }
}
```

**Key Improvements:**
- **Idempotency:** Safe to call multiple times (all operations check for null)
- **Proper Ordering:** Cancel → Stop listener → Wait for task → Dispose
- **Increased Timeout:** 3000ms (from 1000ms) allows in-flight requests to complete
- **Safety Buffer:** Task can complete pending work after cancellation
- **Exception Handling:** `AggregateException` is expected from cancelled tasks

### 5. Remove Obsolete Code

**Delete entirely:**
- `ProcessRequestCallback()` method (lines 360-394) - replaced by async loop
- `_listenerReady` field (line 100) - no longer needed
- `_shouldStop` field (line 103) - replaced by CancellationToken

### 6. Update Constants

**Location:** Line 56

**Current:**
```csharp
public const int ThreadJoinTimeoutMilliseconds = 1000;
```

**New:**
```csharp
public const int TaskWaitTimeoutMilliseconds = 3000;
```

**Usage:** Use in `Cleanup()` at line 341: `_acceptTask.Wait(Constants.TaskWaitTimeoutMilliseconds)`

## Critical Files

| File | Purpose | Changes |
|------|---------|---------|
| `C:\code\Nyamu\Nyamu.UnityPackage\Editor\NyamuServer.cs` | Main HTTP server | Replace threading model (lines 98-103, 219-221, 299-346, 360-411) |
| `C:\code\Nyamu\Documentation\Server with HttpListerner for Unity (chatgpt).md` | Pattern reference | Follow async loop pattern (lines 101-128) |
| `C:\code\Nyamu\Documentation\Port reconnect issue.md` | Context | Ensure TIME_WAIT handling unchanged |

**Unchanged (verify no regression):**
- `C:\code\Nyamu\Nyamu.UnityPackage\Editor\Core\StateManagers\` - All state managers
- `C:\code\Nyamu\Nyamu.UnityPackage\Editor\Core\UnityThreadExecutor.cs` - Main thread dispatch
- `C:\code\Nyamu\Nyamu.UnityPackage\Editor\Tools\` - All MCP tool implementations

## Verification Plan

### 1. Manual Testing (Editor)

**Start/Stop Cycles:**
```
1. Start Unity Editor (triggers Initialize)
2. Verify in Console: "[Nyamu][Server] Server started on port..."
3. Trigger domain reload (modify and save any .cs file)
4. Verify: Server restarts cleanly, no warnings
5. Repeat 10 times
```

**Expected:** No "did not stop gracefully" warnings

**Idempotency Test:**
```
1. In Unity menu: Window > Nyamu > Debug
2. Call Cleanup() twice in succession
3. Call Initialize()
4. Verify: Server starts normally
```

**Expected:** No exceptions, no port conflicts

### 2. Integration Tests (Python)

**Run MCP test suite:**
```bash
cd IntegrationTests
python -m pytest
```

**Critical Tests:**
- `test_scripts_compile.py` - Compilation endpoints
- `test_run_tests.py` - Test execution
- `test_editor_status.py` - Status endpoints
- `test_mcp_initialize.py` - Server startup/shutdown

**Expected:** All 30+ tests pass (same behavior as current implementation)

### 3. MCP Tools Verification

**Using Unity's Nyamu MCP server:**

```bash
# Check compilation
assets_refresh(force=false)
scripts_compile(timeout=30)
scripts_compile_status()

# Check test execution
tests_run_all(test_mode="EditMode", timeout=60)
tests_run_status()

# Check editor status
editor_status()
```

**Expected:** All tools respond within normal timeframes, no exceptions in logs

### 4. Stress Testing

**Concurrent Requests:**
```python
# Python script to send 10 simultaneous MCP requests
import asyncio
import httpx

async def stress_test():
    async with httpx.AsyncClient() as client:
        tasks = [
            client.post("http://localhost:17942/scripts-compile-status")
            for _ in range(10)
        ]
        results = await asyncio.gather(*tasks)
    assert all(r.status_code == 200 for r in results)

asyncio.run(stress_test())
```

**Expected:** All requests succeed, no race condition errors

**Rapid Restart:**
```csharp
// Unity C# script
for (int i = 0; i < 100; i++)
{
    Nyamu.Server.Restart();
    await Task.Delay(100);
}
```

**Expected:** No port conflicts, clean restarts

### 5. Domain Reload Testing

**With Domain Reload Enabled (default):**
```
1. Project Settings > Editor > Enter Play Mode Settings
2. Uncheck "Enter Play Mode Options" (enables domain reload)
3. Enter Play Mode → Exit Play Mode (repeat 3 times)
4. Verify in Console: Server restarts cleanly on each transition
5. Check logs: No "stale state" warnings (static fields reset automatically)
```

**Expected:** Clean restart on each Play Mode transition (static constructor fires)

**With Domain Reload Disabled (critical test per updated ChatGPT doc lines 240-299):**
```
1. Project Settings > Editor > Enter Play Mode Settings
2. Check "Enter Play Mode Options"
3. Uncheck "Reload Domain" (disables domain reload)
4. Enter Play Mode
5. Verify in Console: "[Nyamu][Server] Detected stale state, forcing cleanup"
6. Exit Play Mode
7. Enter Play Mode again
8. Verify: Server still running, defensive check prevented zombie state
9. Check MCP connectivity: Send /scripts-compile-status request
10. Repeat steps 4-9 five times
```

**Expected:**
- Defensive check detects stale state on second+ Play Mode entry
- Server recovers automatically via Cleanup() + Initialize()
- No duplicate tasks, no port conflicts
- MCP endpoints respond normally throughout

**Why This Test Is Critical:** Per ChatGPT doc lines 276-298, without defensive checks the server enters "half-dead zombie state" with cancelled tokens but non-null listeners. Our implementation must prove it self-heals.

## Success Criteria

✅ **Functional:** All integration tests pass (identical MCP behavior)
✅ **Reliability:** Zero "did not stop gracefully" warnings in 100 restart cycles
✅ **Port Handling:** Port rebinds successfully in <5s (TIME_WAIT constraint)
✅ **Code Quality:** No ManualResetEvent or APM pattern usage
✅ **Exception Safety:** No ObjectDisposedException or NullReferenceException in logs

## Risks and Mitigations

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Mono TPL incompatibility | Low | High | Unity 2021.3+ has full async support; existing tools already use `Task<T>` |
| Task survives domain reload | Medium | Medium | Defensive check in Initialize() detects stale state; Cleanup() is idempotent |
| Zombie state (disabled domain reload) | Medium | Medium | Per ChatGPT doc (lines 240-299): static fields persist, threads keep running. Mitigation: defensive check validates all three fields (`_acceptTask`, `_cancellation`, `_listener`) before starting |
| Port binding regression | Low | High | TIME_WAIT logic unchanged; verify with integration tests |
| Performance change | Low | Low | Same threading model (background accept + ThreadPool handlers) |

**Domain Reload Scenarios (per updated documentation):**

1. **Domain Reload Enabled (default):** Static fields reset, `[InitializeOnLoad]` fires → clean restart ✅
2. **Domain Reload Disabled:** Static fields persist, threads survive → defensive check catches stale state and forces cleanup ✅
3. **Multiple Play Mode cycles:** Each transition validated by defensive check ✅

## Rollback Plan

**If issues arise:**
1. Revert commit: `git revert HEAD`
2. Domain reload to restore previous implementation
3. Investigate exception types in logs
4. Recovery time: <5 minutes

**Revert triggers:**
- Integration tests fail with new exception types
- Port binding fails more frequently than current implementation
- Domain reload causes duplicate task instances
