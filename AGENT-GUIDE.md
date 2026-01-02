# Nyamu MCP Server - Agent Guide

This guide provides best practices and workflows for AI coding agents working with the Nyamu MCP server for Unity development.

## Quick Reference

### Critical Workflows

**Creating files:**
```
Write file → assets_refresh(force=false) → Check compilation errors in response
```

**Deleting files:**
```
Delete file → assets_refresh(force=true) → Check compilation errors in response
```

**Editing existing files:**
```
Edit file → scripts_compile (no refresh needed)
```

### Key Points
- **Always** use `assets_refresh` for structural changes (new/deleted/moved files)
- **Never** use `assets_refresh` when only editing existing file contents
- **Error -32603** is expected during compilation/refresh - wait 3-5s and retry
- **Use** `tests_run_regex` for flexible pattern-based test filtering
- **Check status** before long operations to avoid redundant work
- **Progress notifications** are sent for compilation, tests, and shader compilation
- **Editor logs work independently** - read logs even when NyamuServer is unavailable

## Detailed Workflows

### Workflow 1: Creating a New C# Script

```
1. Write the new .cs file to Assets/ directory
2. Call: assets_refresh(force=false)
3. Wait for response (includes compilation error information)
```

**Why this works:**
- Unity needs to be notified about new files in the asset database
- Regular refresh (force=false) is sufficient for new file creation
- Without refresh, Unity won't detect the new file and compilation will fail
- **Response includes compilation errors** - no need to call scripts_compile separately

**Example scenario:**
```
Creating Assets/Scripts/PlayerController.cs
→ assets_refresh(force=false)
→ [Response includes compilation status and errors if any]
```

**Example response:**
```
Asset database refresh completed (including compilation and domain reload).
Compilation FAILED with 1 error:
Assets/Scripts/PlayerController.cs:15 - The name 'undefinedVar' does not exist
Last compilation: 2024-01-15T10:30:45.123Z
```

### Workflow 2: Deleting C# Scripts

```
1. Delete the .cs file and its .meta file
2. Call: assets_refresh(force=true)  ← Note: force=true is critical
3. Wait for response (includes compilation status)
```

**Why force=true is required:**
- Unity may still reference deleted files without force refresh
- Without force=true, you'll get CS2001 "Source file could not be found" errors
- Force refresh uses ImportAssetOptions.ForceUpdate to clear all references
- **Response shows compilation status** - should compile successfully if done correctly

**Example scenario:**
```
Deleting Assets/Scripts/OldController.cs
→ assets_refresh(force=true)  ← Critical: force=true
→ [Response shows compilation completed successfully]
```

**Example response:**
```
Asset database refresh completed (including compilation and domain reload).
Compilation completed successfully with no errors.
Last compilation: 2024-01-15T10:35:00.000Z
```

### Workflow 3: Editing Existing Files

```
1. Edit the .cs file contents
2. Call: scripts_compile(timeout=30)  ← No refresh needed
```

**Why no refresh:**
- Unity automatically detects changes to existing tracked files
- Unnecessary refresh adds ~2-3 seconds overhead
- Direct compilation is faster and more efficient

**Example scenario:**
```
Modifying Assets/Scripts/PlayerController.cs (fixing a bug)
→ scripts_compile(timeout=30)  ← Direct compilation
→ Check for compilation errors
```

### Workflow 4: Moving/Renaming Files

```
1. Move or rename the file
2. Call: assets_refresh(force=true)  ← Treat like deletion
3. Wait for response (includes compilation status)
```

**Why force=true:**
- File moves are treated as delete+create operations
- Unity needs to update all internal references
- Force refresh ensures clean state
- **Response includes compilation errors** to verify the move was successful

## Error Handling

### Error -32603: HTTP Request Failed

**What it means:**
- Unity HTTP server is restarting during compilation or asset refresh
- This is **expected behavior**, not a bug
- Unity restarts the HTTP server to avoid interference with compilation

**How to handle:**
```
1. Detect error: -32603 with message "HTTP request failed"
2. Wait: 3-5 seconds
3. Retry: Same operation
4. Repeat: Up to 3-5 times if needed
```

**When it happens:**
- During `scripts_compile` - Unity is compiling
- During `assets_refresh` - Unity is refreshing asset database
- During `tests_run_*` - Unity Test Runner is initializing

**Example retry pattern:**
```
Attempt 1: scripts_compile → Error -32603
Wait 3 seconds
Attempt 2: scripts_compile → Success
```

### Other Common Errors

**"Unity Editor HTTP server unavailable"**
- Unity Editor is not running
- Nyamu package is not installed
- HTTP server failed to start (check Unity console)

**CS2001: Source file could not be found**
- You deleted a file but didn't use force=true in assets_refresh
- Solution: Call assets_refresh(force=true) and recompile

**Compilation timeout**
- Project is large or has many files
- Solution: Increase timeout to 45-60 seconds

## Testing Patterns

### Running Tests with New Tools

**New test execution tools:**

```
tests_run_single(test_name="MyNamespace.MyTests.MySpecificTest") - Run single test
tests_run_all(test_mode="EditMode") - Run all tests in mode
tests_run_regex(test_filter_regex=".*PlayerController.*") - Run tests matching regex
```

**Prefer tests_run_regex for pattern matching:**

```
Example patterns:
- ".*PlayerController.*" - All tests with "PlayerController" in name
- "Tests\\.Movement\\..*" - All tests in Movement namespace
- ".*(Jump|Run).*" - Tests containing "Jump" or "Run"
```

### EditMode vs PlayMode Tests

**EditMode:**
- Fast execution (~seconds)
- Tests editor-only functionality
- Can be cancelled with `tests_run_cancel`
- Use timeout: 30s

**PlayMode:**
- Full runtime simulation (~minutes)
- Tests actual game behavior
- Cannot be cancelled (Unity API limitation)
- Use timeout: 60-120s

**Example:**
```
Quick verification:
→ tests_run_all(test_mode="EditMode", timeout=30)

Full integration testing:
→ tests_run_all(test_mode="PlayMode", timeout=120)

Single test:
→ tests_run_single(test_name="MyProject.Tests.PlayerControllerTests.TestJump", test_mode="EditMode")

Regex pattern:
→ tests_run_regex(test_filter_regex=".*PlayerController.*", test_mode="EditMode")
```

### Checking Test Status

```
Before running tests:
→ tests_run_status()
→ Check if tests are already running
→ Avoid starting duplicate test runs
```

## Common Patterns

### Pattern 1: Fix Compilation Errors Iteratively

```
1. scripts_compile_status() - Check current state
2. If errors exist, read error messages
3. Edit files to fix errors
4. scripts_compile() - No refresh for edits
5. Repeat until compilation succeeds
```

### Pattern 2: Add New Feature with Tests

```
1. Write new .cs files in Assets/
2. assets_refresh(force=false) - response shows compilation errors
3. Fix any compilation errors (edit + scripts_compile)
4. Write test files in Assets/Tests/
5. assets_refresh(force=false) - response shows compilation errors
6. tests_run_regex(test_filter_regex=".*NewFeature.*", test_mode="EditMode")
7. Fix issues and iterate
```

**Note:** `assets_refresh` returns compilation information, so steps 2 and 5 provide compilation status without needing separate `scripts_compile` calls.

### Pattern 3: Refactor with Safety

```
1. scripts_compile_status() - Ensure clean state
2. tests_run_all() - Run tests before changes
3. Edit files (no refresh needed)
4. scripts_compile()
5. tests_run_all() - Verify nothing broke
6. If tests fail, iterate on fixes
```

### Pattern 4: Clean Up Unused Files

```
1. Identify files to delete
2. Delete files and .meta files
3. assets_refresh(force=true) ← Critical: returns compilation status
4. Verify no CS2001 errors in response
```

**Note:** `assets_refresh(force=true)` returns compilation information showing whether deletion was successful (no CS2001 errors).

## Shader Compilation

### Single Shader
```
shaders_compile_single(shader_name="Standard", timeout=30)
```
- Fuzzy matching supported
- Shows all matches, auto-compiles best match

### Pattern-Based Compilation
```
shaders_compile_singles_regex(pattern="Assets/Shaders/Custom/.*", timeout=120)
```
- Compile subset of shaders
- Faster than compile_all_shaders
- **Supports MCP progress notifications**: When MCP client provides `progressToken` in request `_meta`, receives real-time progress updates during compilation
- Progress updates sent every ~500ms with current shader being compiled

**Example with progress (MCP protocol):**
```
Request with progressToken in _meta:
{
  "params": {
    "arguments": { "pattern": ".*Standard.*" },
    "_meta": { "progressToken": "shader-compile-123" }
  }
}

Progress notifications received:
- {"progressToken": "shader-compile-123", "progress": 10, "total": 50, "message": "Compiling Standard.shader (10/50)"}
- {"progressToken": "shader-compile-123", "progress": 25, "total": 50, "message": "Compiling StandardSpecular.shader (25/50)"}
- ... continues until complete
```

### Avoid compile_all_shaders
- Can take 15+ minutes for URP projects
- Use for final validation only
- Prefer targeted compilation

## Unity Editor Logs

### When to Use Logs

Unity Editor logs are accessible **even when NyamuServer is unavailable** - the logs are standard Unity files that exist independently of the MCP server.

**Use logs when:**
- NyamuServer HTTP connection is down
- Investigating compilation errors that happened earlier
- Debugging Unity Editor startup issues
- Searching for specific warnings or errors
- Understanding Unity package loading sequence

### Available Log Tools

```
editor_log_path() - Get platform-specific log file path
editor_log_head(line_count=100, log_type="all") - Read first N lines
editor_log_tail(line_count=100, log_type="all") - Read last N lines
editor_log_grep(pattern="error", log_type="all", context_lines=0) - Search with regex
```

### Log Types Filter

All log reading tools support filtering by type:
- `"all"` - All log entries (default)
- `"error"` - Only error messages
- `"warning"` - Only warnings
- `"info"` - Only info messages

### Reading Recent Activity

**Get latest logs:**
```
editor_log_tail(line_count=200, log_type="all")
```

**Get only recent errors:**
```
editor_log_tail(line_count=100, log_type="error")
```

**Use cases:**
- Check what Unity is doing right now
- See latest compilation errors
- Find recent warnings
- Debug current editor state

### Searching for Specific Issues

**Search for errors:**
```
editor_log_grep(pattern="NullReferenceException", log_type="error", context_lines=5)
```

**Search for shader issues:**
```
editor_log_grep(pattern="Shader.*failed", log_type="all", context_lines=3)
```

**Search for compilation errors:**
```
editor_log_grep(pattern="CS[0-9]+", log_type="error", line_limit=50)
```

**Parameters:**
- `pattern` - JavaScript regex pattern (case-insensitive by default)
- `case_sensitive` - Enable case-sensitive search (default: false)
- `context_lines` - Show N lines before/after match (default: 0, max: 10)
- `line_limit` - Max matching lines to return (default: 1000, max: 10000)
- `log_type` - Filter by type: all/error/warning/info

### Reading Startup Logs

**Check Unity initialization:**
```
editor_log_head(line_count=500, log_type="all")
```

**Find startup errors:**
```
editor_log_head(line_count=1000, log_type="error")
```

**Use cases:**
- Debug package loading issues
- Find initialization errors
- Verify Unity version and configuration
- Check what packages are loaded

### Finding Log File Location

**Get log path:**
```
editor_log_path()
```

**Platform-specific paths:**
- Windows: `%LOCALAPPDATA%/Unity/Editor/Editor.log`
- Mac: `~/Library/Logs/Unity/Editor.log`
- Linux: `~/.config/unity3d/Editor.log`

### Best Practices for Log Reading

1. **Start with tail** - Most recent activity is usually most relevant
2. **Use log_type filter** - Narrow down to errors/warnings when debugging
3. **Add context_lines** - See surrounding code for stack traces (2-5 lines)
4. **Limit results** - Use line_limit to avoid overwhelming output
5. **Combine filters** - Use pattern + log_type for precise results
6. **Fallback to logs** - When MCP server is down, logs still work

### Example: Debugging Compilation Failure

```
Step 1: Check recent errors
→ editor_log_tail(line_count=100, log_type="error")

Step 2: Search for specific error code
→ editor_log_grep(pattern="CS0246", context_lines=3, log_type="error")

Step 3: See full compilation context
→ editor_log_grep(pattern="CompilerOutput", context_lines=10)
```

### Example: When NyamuServer is Down

```
Scenario: MCP server connection failed

Instead of using:
→ scripts_compile_status() ← Won't work, server is down

Use logs instead:
→ editor_log_tail(line_count=200, log_type="error")
→ editor_log_grep(pattern="compilation|error", context_lines=5)
→ Check for compilation errors manually
```

## Progress Notifications

All long-running MCP operations provide real-time progress updates:

### Compilation Progress
```
scripts_compile sends progress notifications including:
- Assembly count (completed/total)
- Current assembly name
- Elapsed time in seconds
```

**Example progress:**
```
Progress: "Compiled Assembly-CSharp (5/13, 2.3s)"
Progress: "Compiled Assembly-CSharp-Editor (6/13, 2.5s)"
```

### Test Execution Progress
```
tests_run_all, tests_run_single, tests_run_regex send progress including:
- Test count (completed/total)
- Current test name
```

**Example progress:**
```
Progress: "Running MyProject.Tests.PlayerTests.TestJump (1/6)"
Progress: "Running MyProject.Tests.PlayerTests.TestRun (2/6)"
```

### Shader Compilation Progress
```
shaders_compile_single, compile_all_shaders, shaders_compile_singles_regex send progress including:
- Shader count (completed/total)
- Current shader name
```

**Example progress:**
```
Progress: "Compiling Standard.shader (10/50)"
Progress: "Compiling StandardSpecular.shader (11/50)"
```

### Handling Progress in MCP Clients

**Important:** MCP clients must properly handle progress notifications:
- Progress notifications have `method` field but no `id` field
- Actual responses have `id` field matching the request
- Clients should skip progress notifications and wait for the final response

## Troubleshooting

### Issue: "File changes not detected"
**Solution:** Did you call assets_refresh? Required for new/deleted/moved files.

### Issue: "CS2001 errors after deleting file"
**Solution:** Use `assets_refresh(force=true)` when deleting files.

### Issue: "Constant -32603 errors"
**Solution:**
- Wait longer between operations (3-5 seconds)
- Check Unity Editor is responsive
- Verify compilation isn't stuck

### Issue: "Tests not found"
**Solution:**
- Ensure test files are in Assets/ or subdirectories
- Check test filter pattern is correct
- Try without filter first to see all tests

### Issue: "Compilation times out"
**Solution:**
- Increase timeout to 45-60 seconds
- Check Unity console for hangs
- Verify no infinite loops in Unity code

### Issue: "MCP connection lost"
**Solution:**
- Check Unity Editor is still running
- Verify HTTP server port (default: 17932)
- **Use editor logs** - logs work even when MCP server is down:
  - `editor_log_tail(line_count=200, log_type="error")` - Check recent errors
  - `editor_log_grep(pattern="NyamuServer|HTTP", context_lines=5)` - Search for server issues
- Restart Unity Editor if needed

## Best Practices Summary

1. **Always refresh for structural changes** - new/deleted/moved files require assets_refresh
2. **Never refresh for edits** - Editing existing files doesn't need refresh
3. **Use force=true for deletions** - Prevents CS2001 errors
4. **Expect -32603 errors** - Normal during compilation, wait and retry
5. **Check status before operations** - Avoid redundant work
6. **Prefer regex for test filtering** - More flexible than exact matches
7. **Use appropriate timeouts** - EditMode: 30s, PlayMode: 60-120s, Large projects: 45-60s
8. **Iterate on compilation errors** - Edit and compile until clean
9. **Test after refactoring** - Ensure changes don't break functionality
10. **Wait for MCP responsiveness** - After assets_refresh, wait before next operation
11. **Use editor logs as fallback** - Logs work even when MCP server is unavailable

## Additional Resources

- **Package README**: ../README.md
- **API Guide**: ../NyamuServer-API-Guide.md
- **Unity Test Framework**: https://docs.unity3d.com/Manual/testing-editortestsrunner.html
- **MCP Protocol**: Model Context Protocol specification

---

**Version:** 1.0
**Last Updated:** 2025-12-16
**Package:** dev.polyblank.nyamu
