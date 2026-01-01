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
- **Editing existing files**: Edit → `compilation_trigger` (no refresh needed)

### Compilation Tools
- `compilation_trigger` - Trigger C# script compilation (params: timeout, default 30s)
- `compilation_status` - Check compilation status without triggering (no params)
- `assets_refresh` - Force Unity asset database refresh (params: force, default false)
  - Returns compilation error information, showing last compilation status even if no new compilation occurred
  - Use force=true when deleting files to prevent CS2001 errors
  - Use force=false when creating new files
  - **Single command** to refresh assets AND check compilation - no need to call compilation_status separately

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
- `tests_status` - Check test execution status (no params)
- `tests_cancel` - Cancel running tests (params: test_run_guid optional)

Test modes and timeouts:
- EditMode: Fast, editor-only verification (default, use 30s timeout)
- PlayMode: Full runtime simulation (use 60-120s timeout)
- Only EditMode tests can be cancelled via `tests_cancel`

### Shader Compilation Tools
Available shader compilation tools:
- `compile_shader` - Compile single shader with fuzzy name matching (params: shader_name required, timeout)
- `compile_all_shaders` - Compile all shaders (params: timeout, default 120s) - WARNING: Can take 15+ minutes
- `compile_shaders_regex` - Compile shaders matching regex pattern (params: pattern required, timeout)
- `shader_compilation_status` - Check shader compilation status (no params)

### Editor Tools
- `editor_status` - Get Unity Editor status including compilation, test execution, and play mode state (no params)

### Editor Log Tools
Available editor log tools:
- `editor_log_path` - Get Unity Editor log file path (no params)
- `editor_log_head` - Read first N lines (params: line_count, log_type)
- `editor_log_tail` - Read last N lines (params: line_count, log_type)
- `editor_log_grep` - Search log with regex pattern (params: pattern required, case_sensitive, context_lines, line_limit, log_type)

Log types: all (default), error, warning, info

### Status Checking
- Use `compilation_status`, `tests_status`, `shader_compilation_status`, `editor_status` to check state without triggering operations
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

- Editable project source files are located in the `Assets/` directory and its
  subdirectories.
- Read-only package source files are located in `Library/PackageCache/`. **Do not
  modify** files in this directory.
- MCP integration tests are located in the `IntegrationTests/` directory. These Python tests
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
- C# scripts can be compiled via MCP (`compilation_trigger`).
  - Provides real-time progress updates with assembly count and elapsed time
- Compilation status can be retrieved via MCP (`compilation_status`).
  - Includes progress information when compilation is in progress
- Tests can be executed via MCP (`tests_run_all`, `tests_run_single`, `tests_run_regex`).
  - Provides real-time progress updates with test count
- Shaders can be compiled via MCP (`compile_shader`, `compile_all_shaders`, `compile_shaders_regex`).
  - Provides real-time progress updates with shader count
- If an operation requires scene editing or interaction with the Unity Editor,
  provide clear, step-by-step instructions.
- Write all Git commit messages in English.

# Other instructions 

- `mcp-server.js` log is located at `\.nyamu\mcp-server.log`
- When modifing `mcp-server.js` mcp tool reconnection is required.