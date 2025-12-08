# Test Failures - Diagnosis

## Issue: Essential tests skipped

When running `cd McpTests && python -m pytest -m essential`, all 8 tests are skipped with message:
```
Unity not running or HTTP server unavailable
```

## Root Cause

1. **Unity Editor is running** (PID 17880)
2. **HTTP server not responding** at http://localhost:17932
3. **ProjectSettings.asset had old name** "YamuHttpServer" preventing server start
4. **Missing pytest marker** - `essential` marker was not registered in conftest.py

## Fixes Applied

### 1. Fixed ProjectSettings.asset (4 occurrences)
```
D:\code\Yamu\ProjectSettings\ProjectSettings.asset
- Line 16:  productName: YamuHttpServer → NyamuHttpServer
- Line 161: Standalone: com.DefaultCompany.YamuHttpServer → NyamuHttpServer
- Line 754: metroPackageName: YamuHttpServer → NyamuHttpServer
- Line 761: metroApplicationDescription: YamuHttpServer → NyamuHttpServer
```

### 2. Registered missing pytest markers in conftest.py
Added to `pytest_configure()`:
```python
config.addinivalue_line("markers", "essential: core functionality tests")
config.addinivalue_line("markers", "protocol: pure MCP protocol tests")
config.addinivalue_line("markers", "structural: tests that modify Unity project structure")
```

## Next Steps - MUST DO

### Step 1: Restart Unity Editor

Unity needs to reload ProjectSettings.asset to start the HTTP server with the new name.

**Option A: Full restart (recommended)**
1. Close Unity Editor completely
2. Reopen Unity Hub
3. Open project from D:\code\Yamu
4. Wait for compilation to complete

**Option B: Force domain reload**
1. In Unity Editor menu: Assets → Reimport All
2. Wait for compilation

### Step 2: Verify HTTP server is running

After Unity restarts, check:
```bash
curl http://localhost:17932/compile-status
```

Should return JSON response like:
```json
{
  "status": "idle",
  "isCompiling": false,
  "errors": []
}
```

### Step 3: Re-run essential tests

```bash
cd McpTests && python -m pytest -m essential -v
```

Should now run (not skip) all 8 tests:
- test_compilation_errors.py::test_syntax_error_in_test_script
- test_compile_and_wait.py::test_compile_and_wait_basic
- test_compile_test_status_tools.py::test_compile_status_tool_exists
- test_compile_test_status_tools.py::test_test_status_tool_exists
- test_editor_status.py::test_editor_status_tool_exists
- test_mcp_initialize.py::test_mcp_initialize_success
- test_mcp_tools_list.py::test_tools_list_success
- test_run_tests.py::test_run_tests_default_parameters

## Summary

The rename from "Yamu" to "Nyamu" is complete in code, but Unity hasn't reloaded the ProjectSettings.asset changes yet. After Unity restart, the HTTP server should start with the new "NyamuHttpServer" product name and all tests should run.
