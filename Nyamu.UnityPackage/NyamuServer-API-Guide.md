# Nyamu Server API Guide

## HTTP API Endpoints

### Compilation Endpoints

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/scripts-compile` | GET | Trigger compilation | `timeout` (optional) |
| `/scripts-compile-status` | GET | Get compilation status | None |

### Testing Endpoints

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/tests-run-single` | GET/POST | Run a single specific test | `test_name`, `mode`, `timeout` |
| `/tests-run-all` | GET/POST | Run all tests in specified mode | `mode`, `timeout` |
| `/tests-run-regex` | GET/POST | Run tests matching regex pattern | `filter_regex`, `mode`, `timeout` |
| `/tests-run-status` | GET | Get test execution status | None |
| `/tests-run-cancel` | GET | Cancel running tests | `guid` (optional) |

### Asset Management

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/assets-refresh` | GET | Refresh asset database | `force` (optional) |

### Editor Status

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/editor-status` | GET | Get Editor state | None |
| `/editor-enter-play-mode` | GET | Request Editor to enter Play Mode | None |
| `/editor-exit-play-mode` | GET | Request Editor to exit Play Mode | None |

### Shader Compilation

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/shaders-compile-single` | GET | Compile single shader | `shader_name`, `timeout` |
| `/shaders-compile-all` | GET/POST | Compile all shaders | `timeout` |
| `/shaders-compile-regex` | GET/POST | Compile shaders by regex | `pattern`, `timeout` |
| `/shaders-compile-status` | GET | Get shader compilation status | None |

### Menu Execution

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/menu-items-execute` | POST | Execute Unity menu item | `menu_item_path` |

### Code Execution

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/code-execute` | POST | Compile and run an ad-hoc C# snippet | `code`, `mode`, `usings`, `entryPoint`, `runOnMainThread`, `background`, `timeout` |
| `/code-execute-status` | GET | Get status/result of a code_execute run | `execution_id` (optional) |

**Note:** Editor log tools (`editor_log_path`, `editor_log_head`, `editor_log_tail`, `editor_log_grep`) are provided at the MCP layer by mcp-server.js, not as HTTP endpoints. They read the Unity Editor log file directly from the file system.

## Detailed Endpoint Documentation

### Run Single Test

**Endpoint:** `GET /tests-run-single?test_name=MyNamespace.MyTests.MySpecificTest&mode=EditMode`

**Parameters:**
- `test_name` (required): Full name of the test to run
- `mode` (optional): "EditMode" or "PlayMode" (default: "EditMode")
- `timeout` (optional): Timeout in seconds (default: 60)

**Example:**
```bash
GET /tests-run-single?test_name=MyProject.Tests.PlayerControllerTests.TestJump&mode=EditMode
```

### Run All Tests

**Endpoint:** `GET /tests-run-all?mode=EditMode`

**Parameters:**
- `mode` (optional): "EditMode" or "PlayMode" (default: "EditMode")
- `timeout` (optional): Timeout in seconds (default: 60)

**Examples:**
```bash
# Run all EditMode tests
GET /tests-run-all?mode=EditMode

# Run all PlayMode tests
GET /tests-run-all?mode=PlayMode
```

### Run Tests with Regex Filter

**Endpoint:** `GET /tests-run-regex?filter_regex=.*PlayerController.*&mode=EditMode`

**Parameters:**
- `filter_regex` (required): .NET Regex pattern for filtering tests
- `mode` (optional): "EditMode" or "PlayMode" (default: "EditMode")
- `timeout` (optional): Timeout in seconds (default: 60)

**Examples:**
```bash
# Run only integration tests:
GET /tests-run-regex?filter_regex=Integration\.Tests\..*&mode=PlayMode

# Run single test by pattern:
GET /tests-run-regex?filter_regex=MyNamespace\.MyTests\.SpecificTest&mode=EditMode

# Run all tests in a namespace:
GET /tests-run-regex?filter_regex=MyNamespace\..*&mode=EditMode
```

### Editor Status

**Endpoint:** `GET /editor-status`

**Response Fields:**
- `isCompiling`, `isRunningTests`, `isRefreshing`, `isWaitingForCompilation`: booleans
- `isPlaying`: is the Editor currently in Play Mode
- `isPaused`: is the Editor paused (only meaningful while `isPlaying` is true)
- `isEnteringPlayMode`: a Play Mode entry has been requested but is not yet running
- `isExitingPlayMode`: leaving Play Mode, Edit Mode is not yet restored
- `stateAgeSeconds`: seconds since the last `EditorApplication.update` tick; `-1` if never sampled
- `isStateStale`: `true` when `stateAgeSeconds` exceeds ~2s, meaning the Editor's main thread may not be ticking (mid-compile, mid-domain-reload, or blocked by a modal dialog) - treat the other fields as unreliable while this is true
- `lastEditorUpdateUtc`: ISO 8601 timestamp of the last sample; `""` if never sampled

`isEnteringPlayMode` and `isExitingPlayMode` are mutually exclusive and both short-lived: a domain reload begins almost immediately after either transition starts and takes the HTTP server down with it, so most callers observe `false → (connection gap) → new steady state` rather than catching the flag mid-flight.

### Play Mode Transitions

**Endpoints:** `GET /editor-enter-play-mode`, `GET /editor-exit-play-mode`

Both requests are asynchronous by necessity: Unity applies `EditorApplication.isPlaying` at the end of the current editor frame and then reloads the script domain, which stops the Nyamu HTTP server for anywhere from a few hundred milliseconds to several seconds. The response you get back confirms only that **the request reached Unity's main thread**, not that the transition finished - poll `/editor-status` afterwards to confirm the outcome.

**Response Fields:**
- `success`: boolean
- `status`: one of
  - Enter: `requested`, `already_playing`, `blocked` (Unity is compiling), `main_thread_timeout`, `error`
  - Exit: `exit_requested`, `not_playing`, `main_thread_timeout`, `error`
- `message`: human-readable detail
- `wasPlaying`: the Editor's play/transition state at the moment the request was processed

`main_thread_timeout` means the Editor's main thread did not process the request within 3 seconds (compiling, mid-reload, or a blocked modal dialog) - the request may still apply later, so re-check `/editor-status` before retrying.

**Example:**
```bash
# Enter Play Mode, then poll for confirmation
GET /editor-enter-play-mode
# ... wait 3-5s if the server is briefly unreachable ...
GET /editor-status   # confirm isPlaying: true
```

### Code Execution

**Endpoints:** `POST /code-execute`, `GET /code-execute-status`

`POST /code-execute` always answers immediately with `{"status", "executionId", "phase"}` - the HTTP handler only enqueues the work on the Unity main thread, it never blocks for the compile+run itself. Poll `/code-execute-status?execution_id=<id>` for the result.

**Request body (`/code-execute`):**
```json
{
  "code": "AssetDatabase.FindAssets(\"t:Material\").Length",
  "mode": "auto",
  "usings": [],
  "entryPoint": "Execute",
  "runOnMainThread": true,
  "background": false,
  "timeout": 60
}
```
- `mode`: `auto` | `expression` | `statements` | `class`
- `runOnMainThread`: `true` (default) runs on Unity's main thread with full API access, but a blocking snippet freezes the Editor for its duration and cannot be cancelled. `false` runs on a worker thread - never freezes the Editor, but any UnityEngine/UnityEditor API call throws.
- `background`: ignored by the raw HTTP endpoint (it always returns immediately) - this flag only changes polling behaviour at the MCP layer.

**Phases (`/code-execute-status` → `phase`):** `queued` → `compiling` → `compiled` → `executing` → `completed` | `failed`

**Outcome values (`/code-execute-status` → `outcome`):**
| Outcome | Meaning |
|---|---|
| `success` | ran to completion |
| `compile_error` | see `errors[]` (`file`, `line`, `column`, `severity`, `message`) - line numbers are in the snippet's own coordinates, not the generated wrapper's |
| `runtime_exception` | see `exceptionType`, `exceptionMessage`, `stackTrace` |
| `no_entry_point` | `mode: "class"` with no (or an ambiguous) matching entry point method |
| `editor_busy` | Unity was compiling when the run tried to start |
| `build_rejected` | `AssemblyBuilder.Build()` refused to start |
| `main_thread_timeout` | the compiled call never ran on the main thread (domain reload / modal dialog) |
| `async_timeout` | a returned `Task` did not complete in time |
| `worker_thread_timeout` | `runOnMainThread: false` and the snippet did not return in time |
| `unsupported_return` | the snippet returned an `IEnumerator` (not driven in this version) |
| `busy` | another `code_execute` is already in flight |
| `internal_error` | Nyamu's own fault |

**Example:**
```bash
POST /code-execute
{"code": "1 + 1", "mode": "auto", "runOnMainThread": true, "timeout": 60}
# => {"status":"ok","executionId":"…","phase":"queued","message":"Code execution queued."}

GET /code-execute-status?execution_id=…
# => {"status":"ok","phase":"completed","outcome":"success","result":"2","resultType":"System.Int32", ...}
```

### Test Status

**Endpoint:** `GET /tests-run-status`

**Response Fields:**
- `status`: "running" or "idle"
- `isRunning`: boolean
- `lastTestTime`: ISO timestamp
- `testResults`: object with statistics and individual results
- `testRunId`: unique GUID
- `hasError`: boolean
- `errorMessage`: error description if applicable

### Cancel Tests

**Endpoint:** `GET /tests-run-cancel?guid=`

**Parameters:**
- `guid` (optional): Test run GUID to cancel. If not provided, cancels current running test.

**Examples:**
```bash
# Cancel current test run
GET /tests-run-cancel

# Cancel specific test run
GET /tests-run-cancel?guid=abc123def456
```

## Usage Examples

### Basic Workflow

```bash
# Check editor status
GET /editor-status

# Trigger compilation
GET /scripts-compile

# Wait for compilation to complete
GET /scripts-compile-status

# Run all EditMode tests
GET /tests-run-all?mode=EditMode

# Check test status
GET /tests-run-status
```

### CI/CD Integration

```bash
# 1. Verify Unity is ready
GET /editor-status

# 2. Compile project
GET /scripts-compile

# 3. Check compilation status
GET /scripts-compile-status

# 4. Run EditMode tests
GET /tests-run-all?mode=EditMode

# 5. Check test results
GET /tests-run-status

# 6. Run PlayMode tests
GET /tests-run-all?mode=PlayMode

# 7. Check final test results
GET /tests-run-status
```

### Advanced Testing Patterns

```bash
# Run only integration tests:
GET /tests-run-regex?filter_regex=Integration\.Tests\..*&mode=PlayMode

# Run single test:
GET /tests-run-single?test_name=MyNamespace.MyTests.SpecificTest&mode=EditMode

# Run all tests in a namespace:
GET /tests-run-regex?filter_regex=MyNamespace\..*&mode=EditMode
```

## Error Handling

### Common Errors

**HTTP -32603 Errors:**
- Unity HTTP server restarting during compilation/asset refresh
- Expected behavior, wait 3-5 seconds and retry

**Test Execution Issues:**
- Check `/tests-run-status` for detailed error information
- Verify test names and patterns are correct

**Compilation Errors:**
- Check `/scripts-compile-status` for error details
- Fix compilation issues before running tests

## Progress Notifications

Long-running operations send real-time progress updates when accessed via MCP:

### Compilation Progress
- **Assembly count**: Shows completed/total assemblies
- **Current assembly**: Name of assembly being compiled
- **Elapsed time**: Seconds since compilation started

**Example:** `"Compiled Assembly-CSharp (5/13, 2.3s)"`

### Test Execution Progress
- **Test count**: Shows completed/total tests
- **Current test**: Full name of test being executed

**Example:** `"Running MyProject.Tests.PlayerTests.TestJump (1/6)"`

### Shader Compilation Progress
- **Shader count**: Shows completed/total shaders
- **Current shader**: Name of shader being compiled

**Example:** `"Compiling Standard.shader (10/50)"`

### MCP Protocol Details

When using the MCP protocol:
- Progress notifications are sent as JSON-RPC notifications (have `method` field, no `id` field)
- Final responses have `id` field matching the request
- MCP clients should skip progress notifications and wait for the actual response

### Status Endpoints Include Progress

Status endpoints (`/scripts-compile-status`, `/tests-run-status`, `/shaders-compile-status`) return progress information when operations are in progress:

**Example `/scripts-compile-status` with progress:**
```json
{
  "status": "compiling",
  "isCompiling": true,
  "progress": {
    "totalAssemblies": 13,
    "completedAssemblies": 5,
    "currentAssembly": "Assembly-CSharp",
    "elapsedSeconds": 2.3
  }
}
```

## Best Practices

1. **Check status before operations** to avoid redundant work
2. **Use appropriate timeouts** (EditMode: 30s, PlayMode: 60-120s)
3. **Prefer regex filtering** for flexible test selection
4. **Handle -32603 errors** with retry logic
5. **Monitor test status** to avoid duplicate test runs
