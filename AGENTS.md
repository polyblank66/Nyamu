Check out Design.md for the project design and goal definitions.

# About MCP

- This project uses the Nyamu MCP server, which enables triggering compilations
  and retrieving compilation errors from the Unity Editor.
- Iterate through the edit-compile-debug cycle until all errors are resolved.

## Nyamu MCP Workflow Guidelines

### File Operation Workflows
**CRITICAL**: Always call `assets_refresh` after file operations - it returns compilation error information:

- **Creating files**: Write → `assets_refresh(force=false)` → Wait for MCP (response includes compilation errors)
- **Deleting files**: Delete → `assets_refresh(force=true)` → Wait for MCP (response includes compilation errors)
- **Editing existing files**: Edit → `scripts_compile` (no refresh needed)

### Compilation Tools
- `assets_refresh` - **PRIMARY COMPILATION CHECK TOOL** (params: force, default false)
  - Main command to check compilation status after any file operations
  - Returns compilation error information, showing last compilation status even if no new compilation occurred
  - Unified tool: asset refresh + compilation trigger + status check in one command
  - Use force=true when deleting files to prevent CS2001 errors
  - Use force=false when creating/editing files
  - **No need to call scripts_compile_status separately** - this tool provides all compilation information
- `scripts_compile` - Direct C# compilation trigger (params: timeout, default 30s)
  - Use for editing existing files without structural changes (faster than assets_refresh)
- `scripts_compile_status` - Check compilation status without triggering (no params)
  - Use only when you need status check without any file operations

### Error Handling
- **Error -32603 "HTTP request failed"**: Expected during Unity recompilation/refresh
  - Wait 3-5 seconds and retry
  - This is normal behavior - Unity HTTP server is restarting
- Always wait for MCP responsiveness after `assets_refresh` before calling other tools

### Testing Tools
Available test execution tools:
- `tests_run_all` - Run all tests (params: test_mode, timeout)
- `tests_run_single` - Run specific test (params: test_name required, test_mode, timeout)
- `tests_run_regex` - Run tests matching regex (params: test_filter_regex required, test_mode, timeout)
- `tests_run_status` - Check test execution status (no params)
- `tests_run_cancel` - Cancel running tests (params: test_run_guid optional)

Test modes and timeouts:
- EditMode: Fast, editor-only verification (default, use 30s timeout)
- PlayMode: Full runtime simulation (use 60-120s timeout)
- Only EditMode tests can be cancelled via `tests_run_cancel`

### Shader Compilation Tools
Available shader compilation tools:
- `shaders_compile_single` - Compile single shader with fuzzy name matching (params: shader_name required, timeout)
- `shaders_compile_all` - Compile all shaders (params: timeout, default 120s) - WARNING: Can take 15+ minutes
- `shaders_compile_regex` - Compile shaders matching regex pattern (params: pattern required, timeout)
- `shaders_compile_status` - Check shader compilation status (no params)

### Editor Tools
- `editor_status` - Get Unity Editor status including compilation, test execution, and play mode state (no params)
  - Play mode fields: `isPlaying`, `isPaused`, `isEnteringPlayMode`, `isExitingPlayMode`
  - Staleness fields: `stateAgeSeconds`, `isStateStale`, `lastEditorUpdateUtc` - use these to tell whether the cached state is still trustworthy (e.g. during a slow domain reload)
  - Works while the Editor is in Play Mode
- `editor_enter_play_mode` - Request Unity to enter Play Mode (no params)
- `editor_exit_play_mode` - Request Unity to exit Play Mode back to Edit Mode (no params)
  - Both tools report that the request was accepted, not that the transition is complete - poll `editor_status` to confirm
  - Both trigger a domain reload: expect `-32603` for a few seconds after calling; wait 3-5s and retry, then confirm via `editor_status`

### Code Execution Tools
- `code_execute` - Compile and run an ad-hoc C# snippet in the Unity Editor (params: code required, mode, usings, entry_point, run_on_main_thread, timeout, background)
  - Use it to reflect over project types, inspect `Selection`/`AssetDatabase`, or test an idea without entering Play Mode
  - `run_on_main_thread` defaults to `true` - a blocking snippet (infinite loop, long sleep) freezes the Editor for that duration and **cannot be cancelled**. Set `run_on_main_thread: false` for pure computation/reflection that never touches a UnityEngine/UnityEditor API; that path never freezes the Editor
  - Each execution permanently loads a small assembly into the Editor process until the next domain reload (a script compile or Play Mode transition) - expected, bounded, not a bug
  - `Debug.Log`/`Warning`/`Error` output and `Console` stdout/stderr during the run are captured in the response
  - Always asynchronous on the Unity side; the tool call itself polls internally and returns the final result. Set `background: true` to get the `executionId` back immediately instead
  - Only one execution may be in flight at a time
- `code_execute_status` - Fetch the status/result of a run by `execution_id` (params: execution_id, omit for the most recent run)
  - Use to poll a `background: true` execution, or to re-check a result after a `code_execute` call timed out waiting

### Editor Log Tools
Available editor log tools:
- `editor_log_path` - Get Unity Editor log file path (no params)
- `editor_log_head` - Read first N lines (params: line_count, log_type)
- `editor_log_tail` - Read last N lines (params: line_count, log_type)
- `editor_log_grep` - Search log with regex pattern (params: pattern required, case_sensitive, context_lines, line_limit, log_type)

Log types: all (default), error, warning, info

### Status Checking
- Use `scripts_compile_status`, `tests_run_status`, `shaders_compile_status`, `editor_status` to check state without triggering operations
- Check status before long operations to avoid redundant work
- Status tools include progress information when operations are in progress

### Progress Notifications
- All long-running operations (compilation, tests, shader compilation) send MCP progress notifications
- Progress notifications are JSON-RPC notifications (have `method` field but no `id` field)
- MCP clients must skip progress notifications and wait for the actual response (has `id` field)
- Progress includes:
  - **Compilation**: Assembly count, current assembly name, elapsed time
  - **Tests**: Test count, current test name
  - **Shader compilation**: Shader count, current shader name

# Technology Choices

- The project is built with Unity.
- We use the Universal Render Pipeline (URP) for rendering.
- UI Toolkit is used for building runtime user interfaces.
- IMGUI may be used for editor-only UI elements.

# Directory Structure

- Unity project files are located in the `Nyamu.UnityTestProject/` directory.
- Editable project source files are located in the `Nyamu.UnityTestProject/Assets/` directory and its
  subdirectories.
- Read-only package source files are located in `Nyamu.UnityTestProject/Library/PackageCache/`. **Do not
  modify** files in this directory.
- MCP integration tests are located in the `IntegrationTests/` directory (at project root). These Python tests
  verify MCP server functionality including compilation, test execution, and response
  formatting. Run tests with `cd IntegrationTests && python -m pytest`.

# Code Style Guidelines

- All comments must be written in English.
- Use `var` for local variables whenever the type can be inferred.
- Omit the `private` access modifier when it is implicit and does not harm
  clarity.
- Omit braces for single-statement blocks (`if`, `for`, `while`, etc.).
- Use expression-bodied members whenever appropriate (for properties, methods,
  lambdas, etc.).
- Don't put trailing whitespaces.

# Workflow Instructions

- Focus primarily on writing or modifying source code.
- C# scripts can be compiled via MCP (`scripts_compile`).
  - Provides real-time progress updates with assembly count and elapsed time
- Compilation status can be retrieved via MCP (`scripts_compile_status`).
  - Includes progress information when compilation is in progress
- Tests can be executed via MCP (`tests_run_all`, `tests_run_single`, `tests_run_regex`).
  - Provides real-time progress updates with test count
- Shaders can be compiled via MCP (`shaders_compile_single`, `shaders_compile_all`, `shaders_compile_regex`).
  - Provides real-time progress updates with shader count
- If an operation requires scene editing or interaction with the Unity Editor,
  provide clear, step-by-step instructions.
- Write all Git commit messages in English.

# Other instructions

- `mcp-server.js` log is located at `Nyamu.UnityTestProject\.nyamu\mcp-server.log`
- When modifing `mcp-server.js` mcp tool reconnection is required.