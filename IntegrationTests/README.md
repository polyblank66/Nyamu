# YAMU MCP Server Integration Tests

This directory contains comprehensive integration tests for the YAMU MCP (Model Context Protocol) server. The tests verify that the MCP server correctly handles compilation and test execution requests from external tools.

## 🚀 Performance Optimized Test Suite

The test suite features a **three-tier cleanup system** for dramatic performance improvements:
- **Protocol tests**: ~90% faster (0.4s vs 4.5s per test)
- **Essential test suite**: ~50% faster (20s vs 36s for 5 tests)
- **Overall suite**: 60-70% faster execution
- **Full randomized testing support** with `pytest-random-order`

## Prerequisites

1. **Unity Editor** must be running with the YAMU project open
2. **Python 3.7+** installed
3. **Node.js** installed (for MCP server)

## Setup

1. Install Python dependencies:
```bash
cd McpTests
pip install -r requirements.txt
```

2. Ensure Unity Editor is running and the YAMU HTTP server is active (should start automatically)

3. Verify Unity HTTP server is accessible:
```bash
curl http://localhost:17932/compile-status
```

## Running Tests

### Run All Tests
```bash
pytest
```

### Run Specific Test Categories

#### 🎯 Optimized Test Selection
```bash
# Essential tests (fastest core functionality - ~20s)
pytest -m essential

# Pure MCP protocol tests (ultra-fast - ~0.4s per test)
pytest -m protocol

# Structural tests (file modifications - full cleanup)
pytest -m structural

# Legacy categories
pytest -m mcp           # MCP functionality tests
pytest -m compilation   # Unity compilation tests
pytest -m asmdef        # Assembly Definition tests
pytest -m slow          # Long-running tests
```

#### 🔀 Randomized Testing
```bash
# Run tests in random order (great for finding test dependencies)
pytest --random-order

# Run with specific seed (reproducible random order)
pytest --random-order-seed=12345

# Run essential tests randomly
pytest -m essential --random-order
```

### Run Specific Test Files
```bash
# Basic MCP operations
pytest test_mcp_initialize.py
pytest test_mcp_tools_list.py
pytest test_compile_and_wait.py
pytest test_run_tests.py

# Compilation error tests
pytest test_compilation_errors.py
pytest test_new_files_with_errors.py

# TestModule asmdef tests
pytest test_asmdef_compilation.py
pytest test_asmdef_errors.py

# Refresh behavior tests
pytest test_refresh_behavior.py

# Status endpoint tests
pytest test_compile_status.py
pytest test_test_status.py
pytest test_editor_status.py

# Test execution and cancellation
pytest test_run_tests_filters.py
pytest test_tests_cancel.py

# MCP response handling
pytest test_response_truncation.py
pytest test_mcp_warning_messages.py
pytest test_compile_test_status_tools.py
```

### Run with Verbose Output
```bash
pytest -v
```

### Run with Coverage
```bash
pytest --cov=. --cov-report=html
```

## Test Categories

### 🔍 Test Classification System

Tests are automatically classified into three performance tiers using pytest markers:

#### 1. **Protocol Tests** (`@pytest.mark.protocol`) - Ultra-Fast
- **Cleanup**: No-op (skips Unity state management)
- **Performance**: ~0.4s per test (90% faster)
- **Coverage**: Pure MCP communication testing
- **Examples**: `test_mcp_initialize.py`, `test_tools_list.py`, `test_*_status.py`

#### 2. **Structural Tests** (`@pytest.mark.structural`) - Full Cleanup
- **Cleanup**: Complete asset refresh and compilation verification
- **Performance**: Normal timing (proper isolation)
- **Coverage**: Tests that create/modify/delete Unity files
- **Examples**: `test_new_files_with_errors.py`, `test_asmdef_*.py`

#### 3. **Minimal Tests** (default) - Fast
- **Cleanup**: Minimal wait (0.1s)
- **Performance**: Very fast
- **Coverage**: Compilation tests that don't modify file structure
- **Examples**: Basic `test_compile_and_wait.py` tests

### 🚀 Legacy Test Categories

#### 1. Basic MCP Operations (`test_mcp_*.py`)
- MCP server initialization
- Tools list retrieval
- Basic compile and test commands
- Protocol compliance

### 2. Compilation Tests (`test_compile_*.py`)
- Successful compilation
- Compilation error handling
- Status monitoring
- Timeout handling

### 3. Test Execution (`test_run_tests.py`, `test_run_tests_filters.py`)
- EditMode test execution
- PlayMode test execution
- Test result formatting
- Test filtering and regex patterns

### 4. Test Cancellation (`test_tests_cancel.py`)
- EditMode test cancellation using TestRunnerApi.CancelTestRun
- GUID-based test run cancellation
- Current test run cancellation
- Error handling for invalid GUIDs
- Edge case testing (no running tests, concurrent access)

### 5. Compilation Error Scenarios

#### Standalone Scripts (`test_compilation_errors.py`, `test_new_files_with_errors.py`)
- Syntax errors in existing files
- Missing using statements
- Undefined variables
- Creating new files with errors
- Multiple error handling

#### Assembly Definition Files (`test_asmdef_*.py`)
- Errors in TestModule assembly
- TestModule script modifications
- New file creation in TestModule
- Assembly isolation testing
- Dependency error handling

### 6. Status Endpoints (`test_*_status.py`)
- Direct HTTP endpoint testing
- Response structure validation
- Status tracking (compile_status, test_status, editor_status)
- CORS header verification
- Unity Editor state monitoring

### 7. MCP Response Handling
- Response truncation testing (`test_response_truncation.py`)
- Warning message handling (`test_mcp_warning_messages.py`)
- Status tools integration (`test_compile_test_status_tools.py`)

## Test Structure

- `conftest.py` - Pytest configuration, smart cleanup fixtures, and UnityStateManager
- `mcp_client.py` - MCP protocol client with comprehensive error handling and retry logic
- `unity_helper.py` - Unity project file manipulation utilities and state management
- `requirements.txt` - Python dependencies including `pytest-random-order`
- `pytest.ini` - Pytest markers including `essential`, `protocol`, and `structural`

## Key Features Tested

1. **MCP Protocol Compliance**
   - JSON-RPC 2.0 format
   - Tool discovery and execution
   - Error handling
   - Tool parameter validation

2. **Compilation Integration**
   - Script compilation triggering
   - Error collection and reporting
   - Multi-file error handling
   - Assembly definition support

3. **Test Execution**
   - Unity Test Runner integration
   - Both EditMode and PlayMode tests
   - Result collection and formatting

4. **Asset Database Management**
   - File creation and deletion tracking
   - Unity AssetDatabase.Refresh() integration
   - Force refresh for file deletions (CS2001 error prevention)
   - Meta file cleanup

5. **File System Integration**
   - New file creation and compilation
   - File modification and error introduction
   - Automatic cleanup and restoration
   - Directory creation and cleanup

6. **Error Scenarios**
   - Syntax errors
   - Missing dependencies
   - Undefined variables
   - Complex multi-error scenarios
   - Unity file tracking issues (CS2001)

## 🔧 Advanced Test Infrastructure

### Smart State Management System

The test suite features an intelligent **UnityStateManager** that automatically selects appropriate cleanup levels:

```python
# Automatic cleanup level detection based on test markers
@pytest.mark.protocol        # → noop cleanup (skip Unity operations)
@pytest.mark.structural      # → full cleanup (complete isolation)
# No specific marker          # → minimal cleanup (fast wait)
```

### Three-Tier Cleanup System

#### 1. **No-Op Cleanup** (Protocol Tests)
- **Action**: Skip all Unity state operations
- **Time**: ~0.1s overhead
- **Use**: Pure MCP communication tests

#### 2. **Minimal Cleanup** (Default)
- **Action**: Brief wait for Unity to settle
- **Time**: ~0.1s overhead
- **Use**: Compilation tests without file modifications

#### 3. **Full Cleanup** (Structural Tests)
- **Action**: Complete asset refresh + compilation verification
- **Time**: ~10-15s overhead (proper isolation)
- **Use**: Tests that create/modify/delete Unity files

### Advanced Error Handling

The MCP client features comprehensive retry logic for Unity HTTP server restarts:
- **Automatic detection** of -32603 errors during Unity compilation
- **Exponential backoff** retry strategy (up to 5 attempts)
- **Timeout error protection** prevents masking legitimate timeouts
- **Progress reporting** shows retry attempts in test output

### Test Data Management

Tests automatically:
- Create backup copies of modified files
- Restore original files after each test
- Clean up temporary files and directories
- Handle Unity .meta file management
- Use force refresh after file deletions to prevent CS2001 errors
- Maintain clean Unity project state between tests with smart cleanup selection

### File Cleanup Best Practices

The test infrastructure uses advanced cleanup patterns to avoid Unity file tracking issues:

```python
# For file deletions - use force refresh
await unity_helper.cleanup_temp_files_with_refresh([file_paths])

# For new file creation - regular refresh is sufficient
await unity_helper.refresh_assets_if_available(force=False)

# Smart cleanup selection (automatic)
# Protocol tests: await manager.ensure_clean_state(cleanup_level="noop")
# Structural tests: await manager.ensure_clean_state(cleanup_level="full")
# Default tests: await manager.ensure_clean_state(cleanup_level="minimal")
```

This prevents Unity CS2001 "Source file could not be found" errors that occur when Unity's AssetDatabase cache becomes out of sync with the file system.

## MCP Tools Reference

The YAMU MCP server provides the following tools:

### 1. `compile_and_wait`
Triggers Unity script compilation and waits for completion.

**Parameters:**
- `timeout` (optional, default: 30): Timeout in seconds

**Usage:**
```python
response = await mcp_client.compile_and_wait(timeout=30)
```

**Important:** For structural changes (new/deleted files), call `refresh_assets` first (use `force=true` for deletions), wait for MCP responsiveness, then call this tool.

### 2. `run_tests`
Executes Unity tests and waits for completion.

**Parameters:**
- `test_mode` (optional, default: "PlayMode"): "EditMode" or "PlayMode"
- `test_filter` (optional): Test filter pattern
- `timeout` (optional, default: 60): Timeout in seconds

**Usage:**
```python
response = await mcp_client.run_tests(
    test_mode="PlayMode",
    test_filter="MyTestClass",
    timeout=60
)
```

### 3. `refresh_assets`
Forces Unity to refresh the asset database. **Required before compilation when files are added/removed/moved.**

**Parameters:**
- `force` (optional, default: false): Use `ImportAssetOptions.ForceUpdate` for stronger refresh (recommended for file deletions)

**Usage:**
```python
# For new file creation
await mcp_client.refresh_assets(force=False)

# For file deletions (recommended to prevent CS2001 errors)
await mcp_client.refresh_assets(force=True)
```

**Workflow for structural changes:**
1. Make file changes (add/remove/move files)
2. Call `refresh_assets` (with `force=true` if deleting files)
3. Wait for MCP to be responsive
4. Call `compile_and_wait`

### 4. `tests_cancel`
Cancels running Unity test execution using Unity's TestRunnerApi.CancelTestRun.

**Parameters:**
- `test_run_guid` (optional): GUID of the test run to cancel. If not provided, cancels the current running test

**Usage:**
```python
# Cancel current running test
response = await mcp_client.cancel_tests()

# Cancel specific test run by GUID
response = await mcp_client.cancel_tests(test_run_guid="12345678-abcd-1234-abcd-123456789abc")
```

**Important limitations:**
- **EditMode tests only**: Currently only supports cancelling EditMode tests (Unity API limitation)
- **PlayMode tests**: Cannot be cancelled due to Unity's TestRunnerApi constraints
- **No active test**: Returns warning if no test is currently running

**Response types:**
- `"status": "ok"`: Cancellation requested successfully
- `"status": "warning"`: No test running or no GUID available
- `"status": "error"`: Failed to cancel or invalid GUID

### 5. `editor_status`
Get current Unity Editor status including compilation state, test execution state, and play mode state.

**Parameters:** None

**Usage:**
```python
response = await mcp_client.editor_status()
```

**Returns:** Real-time Unity Editor state information including whether the editor is compiling, running tests, or in play mode.

### 6. `compile_status`
Get current compilation status without triggering compilation.

**Parameters:** None

**Usage:**
```python
response = await mcp_client.compile_status()
```

**Returns:** Compilation state, last compile time, and any compilation errors.

### 7. `test_status`
Get current test execution status without running tests.

**Parameters:** None

**Usage:**
```python
response = await mcp_client.test_status()
```

**Returns:** Test execution state, last test time, test results, and test run ID.

## Understanding MCP Error -32603

When using YAMU MCP tools, you may encounter **MCP Error -32603** with message "Tool execution failed: HTTP request failed". **This is expected behavior**, not a bug.

### Why This Happens:
- Unity's HTTP server **automatically restarts** during script compilation
- Unity's HTTP server **restarts** during asset database refresh operations
- This prevents interference with Unity's compilation process

### How Tests Handle This:
The MCP client automatically implements retry logic for -32603 errors:
1. **Detects the error** - Recognizes Unity HTTP server restart
2. **Waits 3 seconds** - Allows compilation/refresh to progress
3. **Retries the command** - Up to 5 times with exponential backoff
4. **Reports progress** - Shows retry attempts in test output

### Expected Test Behavior:
- Tests may show "Unity HTTP server restarting" messages
- This is normal and indicates the retry system is working
- Tests will automatically succeed once Unity completes compilation

### Manual Testing:
If testing manually without retry logic, follow this pattern:
```python
for attempt in range(5):
    try:
        response = await mcp_client.compile_and_wait()
        break  # Success
    except RuntimeError as e:
        if "-32603" in str(e) and "HTTP request failed" in str(e):
            print(f"Unity compiling, retrying in 3s... (attempt {attempt + 1}/5)")
            await asyncio.sleep(3)
        else:
            raise  # Different error
```

## Troubleshooting

### Unity Not Responding
- Ensure Unity Editor is open and responsive
- Check that the YAMU package is properly installed
- Verify HTTP server is running on port 17932

### Test Failures
- Check Unity Console for compilation errors
- Ensure no pending compilation is running
- Verify file permissions for test file creation
- If seeing CS2001 errors, try running with force refresh: `await mcp_client.refresh_assets(force=True)`

### Persistent -32603 Errors
If -32603 errors persist beyond retry attempts:
- Unity may be stuck in compilation loop
- Check Unity Console for compilation errors
- Restart Unity Editor to reset HTTP server
- Verify Unity is not processing large asset imports

### CS2001 "Source file could not be found" Errors
This indicates Unity's AssetDatabase is out of sync with the file system:
- Use `refresh_assets(force=True)` after file deletions
- Check that temporary test files were properly cleaned up
- Restart Unity Editor if the issue persists

### Slow Test Performance ⚡ IMPROVED

The test suite has been dramatically optimized:

```bash
# Run ultra-fast protocol tests only (~0.4s per test)
pytest -m protocol

# Run fast essential tests (~20s total)
pytest -m essential

# Skip slow tests entirely
pytest -m "not slow"

# Run with performance monitoring
pytest -v --tb=short  # Shows cleanup level detection
```

**Performance comparison:**
- **Before optimization**: 4-15s per test
- **After optimization**: 0.4s (protocol), 2s (minimal), appropriate timing (structural)
- **Essential test suite**: 20s vs previous 36s (44% improvement)

If tests are still slow:
- Increase timeout values if needed
- Ensure Unity has sufficient resources
- Check for Unity compilation loops
- Use `pytest-random-order` to identify test dependencies

## Continuous Integration

### ⚡ Optimized CI Commands

For CI environments, use the optimized test selection:

```bash
# Ultra-fast CI run (protocol + essential tests - ~25s total)
pytest -m "essential or protocol" --random-order

# Fast comprehensive run (exclude only slow tests - ~2-3 minutes)
pytest -m "not slow" --random-order

# Full test suite with randomization (comprehensive - ~5-8 minutes)
pytest --random-order --timeout=600

# Generate reports
pytest -m essential --junitxml=test-results.xml --cov=. --cov-report=xml

# Performance monitoring
pytest -v --tb=short -m essential  # Shows cleanup level detection
```

### CI Performance Benefits

- **Essential tests**: ~25s (vs previous ~60s)
- **Protocol tests**: ~15s for 24 tests (vs previous ~2 minutes)
- **Non-slow tests**: ~3 minutes (vs previous ~8 minutes)
- **Full randomized suite**: Reliable execution without test dependencies

## Contributing

### 🎯 Adding New Tests

When adding new tests, use the **performance-tier markers** for optimal execution speed:

#### 1. **Protocol Tests** (Pure MCP Communication)
```python
@pytest.mark.mcp
@pytest.mark.protocol  # ← Ultra-fast cleanup
@pytest.mark.asyncio
async def test_my_mcp_feature(mcp_client, unity_state_manager):
    """Test MCP protocol communication only"""
    response = await mcp_client.some_mcp_call()
    assert response["jsonrpc"] == "2.0"
```

#### 2. **Structural Tests** (File Modifications)
```python
@pytest.mark.compilation
@pytest.mark.structural  # ← Full cleanup for file changes
@pytest.mark.asyncio
async def test_my_file_modification(mcp_client, unity_helper, unity_state_manager, temp_files):
    """Test that creates/modifies/deletes Unity files"""
    script_path = unity_helper.create_temp_script_in_assets("MyTest", "syntax")
    temp_files(script_path)
    # Test logic here
```

#### 3. **Minimal Tests** (Compilation Only)
```python
@pytest.mark.compilation  # ← No performance marker = minimal cleanup
@pytest.mark.asyncio
async def test_my_compilation_feature(mcp_client, unity_state_manager):
    """Test that triggers compilation but doesn't modify files"""
    response = await mcp_client.compile_and_wait(timeout=30)
    assert "Compilation completed" in response["result"]["content"][0]["text"]
```

### ✅ Test Development Guidelines

1. **Always use performance-appropriate markers**:
   - `@pytest.mark.protocol` for pure MCP communication
   - `@pytest.mark.structural` for file modifications
   - No performance marker for compilation-only tests

2. **Essential tests**: Add `@pytest.mark.essential` for core functionality

3. **Use fixtures appropriately**:
   - `unity_state_manager` - Always include (automatic cleanup selection)
   - `unity_helper` - For file manipulation utilities
   - `temp_files` - For automatic file cleanup registration

4. **Test isolation**:
   - Tests must work in any random order (`pytest --random-order`)
   - No dependencies between tests
   - Clean state after each test

5. **Documentation**:
   - Include docstrings explaining test purpose
   - Follow existing naming conventions
   - Add comments for complex test logic

### 🔍 Performance Testing

Test your additions:
```bash
# Verify your protocol test gets noop cleanup
pytest test_my_protocol_test.py -v -s  # Should show "noop cleanup"

# Verify your structural test gets full cleanup
pytest test_my_structural_test.py -v -s  # Should show "full cleanup"

# Test in random order
pytest test_my_new_tests.py --random-order-seed=12345
```