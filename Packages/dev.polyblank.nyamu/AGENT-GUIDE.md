# Nyamu MCP Server - Agent Guide

This guide provides best practices and workflows for AI coding agents working with the Nyamu MCP server for Unity development.

## Quick Reference

### Critical Workflows

**Creating files:**
```
Write file → refresh_assets(force=false) → Wait for MCP → compilation_trigger
```

**Deleting files:**
```
Delete file → refresh_assets(force=true) → Wait for MCP → compilation_trigger
```

**Editing existing files:**
```
Edit file → compilation_trigger (no refresh needed)
```

### Key Points
- **Always** use `refresh_assets` for structural changes (new/deleted/moved files)
- **Never** use `refresh_assets` when only editing existing file contents
- **Error -32603** is expected during compilation/refresh - wait 3-5s and retry
- **Prefer** `test_filter_regex` over `test_filter` for pattern matching
- **Check status** before long operations to avoid redundant work

## Detailed Workflows

### Workflow 1: Creating a New C# Script

```
1. Write the new .cs file to Assets/ directory
2. Call: refresh_assets(force=false)
3. Wait for MCP to become responsive again
4. Call: compilation_trigger(timeout=30)
5. Handle compilation results
```

**Why this works:**
- Unity needs to be notified about new files in the asset database
- Regular refresh (force=false) is sufficient for new file creation
- Without refresh, Unity won't detect the new file and compilation will fail

**Example scenario:**
```
Creating Assets/Scripts/PlayerController.cs
→ refresh_assets(force=false)
→ [Wait ~2-3 seconds for Unity to process]
→ compilation_trigger(timeout=30)
→ Check for compilation errors
```

### Workflow 2: Deleting C# Scripts

```
1. Delete the .cs file and its .meta file
2. Call: refresh_assets(force=true)  ← Note: force=true is critical
3. Wait for MCP to become responsive
4. Call: compilation_trigger(timeout=30)
```

**Why force=true is required:**
- Unity may still reference deleted files without force refresh
- Without force=true, you'll get CS2001 "Source file could not be found" errors
- Force refresh uses ImportAssetOptions.ForceUpdate to clear all references

**Example scenario:**
```
Deleting Assets/Scripts/OldController.cs
→ refresh_assets(force=true)  ← Critical: force=true
→ [Wait ~2-3 seconds]
→ compilation_trigger(timeout=30)
→ Should compile successfully (no CS2001 errors)
```

### Workflow 3: Editing Existing Files

```
1. Edit the .cs file contents
2. Call: compilation_trigger(timeout=30)  ← No refresh needed
```

**Why no refresh:**
- Unity automatically detects changes to existing tracked files
- Unnecessary refresh adds ~2-3 seconds overhead
- Direct compilation is faster and more efficient

**Example scenario:**
```
Modifying Assets/Scripts/PlayerController.cs (fixing a bug)
→ compilation_trigger(timeout=30)  ← Direct compilation
→ Check for compilation errors
```

### Workflow 4: Moving/Renaming Files

```
1. Move or rename the file
2. Call: refresh_assets(force=true)  ← Treat like deletion
3. Wait for MCP responsiveness
4. Call: compilation_trigger(timeout=30)
```

**Why force=true:**
- File moves are treated as delete+create operations
- Unity needs to update all internal references
- Force refresh ensures clean state

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
- During `compilation_trigger` - Unity is compiling
- During `refresh_assets` - Unity is refreshing asset database
- During `run_tests` - Unity Test Runner is initializing

**Example retry pattern:**
```
Attempt 1: compilation_trigger → Error -32603
Wait 3 seconds
Attempt 2: compilation_trigger → Success
```

### Other Common Errors

**"Unity Editor HTTP server unavailable"**
- Unity Editor is not running
- Nyamu package is not installed
- HTTP server failed to start (check Unity console)

**CS2001: Source file could not be found**
- You deleted a file but didn't use force=true in refresh_assets
- Solution: Call refresh_assets(force=true) and recompile

**Compilation timeout**
- Project is large or has many files
- Solution: Increase timeout to 45-60 seconds

## Testing Patterns

### Running Tests with Filters

**Prefer test_filter_regex for pattern matching:**

```
Example patterns:
- ".*PlayerController.*" - All tests with "PlayerController" in name
- "Tests\\.Movement\\..*" - All tests in Movement namespace
- ".*(Jump|Run).*" - Tests containing "Jump" or "Run"
```

**Use test_filter for exact matches:**
```
Example:
- "MyProject.Tests.PlayerControllerTests.TestJump"
- "MyProject.Tests.PlayerControllerTests.TestRun|MyProject.Tests.EnemyTests.TestAI"
```

### EditMode vs PlayMode Tests

**EditMode:**
- Fast execution (~seconds)
- Tests editor-only functionality
- Can be cancelled with `tests_cancel`
- Use timeout: 30s

**PlayMode:**
- Full runtime simulation (~minutes)
- Tests actual game behavior
- Cannot be cancelled (Unity API limitation)
- Use timeout: 60-120s

**Example:**
```
Quick verification:
→ run_tests(test_mode="EditMode", timeout=30)

Full integration testing:
→ run_tests(test_mode="PlayMode", timeout=120)
```

### Checking Test Status

```
Before running tests:
→ test_status()
→ Check if tests are already running
→ Avoid starting duplicate test runs
```

## Common Patterns

### Pattern 1: Fix Compilation Errors Iteratively

```
1. compilation_status() - Check current state
2. If errors exist, read error messages
3. Edit files to fix errors
4. compilation_trigger() - No refresh for edits
5. Repeat until compilation succeeds
```

### Pattern 2: Add New Feature with Tests

```
1. Write new .cs files in Assets/
2. refresh_assets(force=false)
3. Wait for MCP
4. compilation_trigger(timeout=30)
5. Fix any compilation errors (edit + compile)
6. Write test files in Assets/Tests/
7. refresh_assets(force=false)
8. compilation_trigger(timeout=30)
9. run_tests(test_filter_regex=".*NewFeature.*", test_mode="EditMode")
10. Fix issues and iterate
```

### Pattern 3: Refactor with Safety

```
1. compilation_status() - Ensure clean state
2. run_tests() - Run tests before changes
3. Edit files (no refresh needed)
4. compilation_trigger()
5. run_tests() - Verify nothing broke
6. If tests fail, iterate on fixes
```

### Pattern 4: Clean Up Unused Files

```
1. Identify files to delete
2. Delete files and .meta files
3. refresh_assets(force=true) ← Critical
4. Wait for MCP
5. compilation_trigger()
6. Verify no CS2001 errors
```

## Shader Compilation

### Single Shader
```
compile_shader(shader_name="Standard", timeout=30)
```
- Fuzzy matching supported
- Shows all matches, auto-compiles best match

### Pattern-Based Compilation
```
compile_shaders_regex(pattern="Assets/Shaders/Custom/.*", timeout=120)
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

## Troubleshooting

### Issue: "File changes not detected"
**Solution:** Did you call refresh_assets? Required for new/deleted/moved files.

### Issue: "CS2001 errors after deleting file"
**Solution:** Use `refresh_assets(force=true)` when deleting files.

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
- Restart Unity Editor if needed

## Best Practices Summary

1. **Always refresh for structural changes** - new/deleted/moved files require refresh_assets
2. **Never refresh for edits** - Editing existing files doesn't need refresh
3. **Use force=true for deletions** - Prevents CS2001 errors
4. **Expect -32603 errors** - Normal during compilation, wait and retry
5. **Check status before operations** - Avoid redundant work
6. **Prefer regex for test filtering** - More flexible than exact matches
7. **Use appropriate timeouts** - EditMode: 30s, PlayMode: 60-120s, Large projects: 45-60s
8. **Iterate on compilation errors** - Edit and compile until clean
9. **Test after refactoring** - Ensure changes don't break functionality
10. **Wait for MCP responsiveness** - After refresh_assets, wait before next operation

## Additional Resources

- **Package README**: ../README.md
- **API Guide**: ../NyamuServer-API-Guide.md
- **Unity Test Framework**: https://docs.unity3d.com/Manual/testing-editortestsrunner.html
- **MCP Protocol**: Model Context Protocol specification

---

**Version:** 1.0
**Last Updated:** 2025-12-16
**Package:** dev.polyblank.nyamu
