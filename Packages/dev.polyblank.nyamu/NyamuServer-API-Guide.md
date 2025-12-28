# Nyamu Server API Guide

## HTTP API Endpoints

### Compilation Endpoints

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/compilation-trigger` | GET | Trigger compilation | `timeout` (optional) |
| `/compilation-status` | GET | Get compilation status | None |

### Testing Endpoints

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/tests-run-single` | GET/POST | Run a single specific test | `test_name`, `mode`, `timeout` |
| `/tests-run-all` | GET/POST | Run all tests in specified mode | `mode`, `timeout` |
| `/tests-run-regex` | GET/POST | Run tests matching regex pattern | `filter_regex`, `mode`, `timeout` |
| `/tests-status` | GET | Get test execution status | None |
| `/tests-cancel` | GET | Cancel running tests | `guid` (optional) |

### Asset Management

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/refresh-assets` | GET | Refresh asset database | `force` (optional) |

### Editor Status

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/editor-status` | GET | Get Editor state | None |

### Shader Compilation

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/compile-shader` | GET | Compile single shader | `shader_name`, `timeout` |
| `/compile-all-shaders` | GET/POST | Compile all shaders | `timeout` |
| `/compile-shaders-regex` | GET/POST | Compile shaders by regex | `pattern`, `timeout` |
| `/shader-compilation-status` | GET | Get shader compilation status | None |

### Menu Execution

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/execute-menu-item` | POST | Execute Unity menu item | `menu_item_path` |

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

### Test Status

**Endpoint:** `GET /tests-status`

**Response Fields:**
- `status`: "running" or "idle"
- `isRunning`: boolean
- `lastTestTime`: ISO timestamp
- `testResults`: object with statistics and individual results
- `testRunId`: unique GUID
- `hasError`: boolean
- `errorMessage`: error description if applicable

### Cancel Tests

**Endpoint:** `GET /tests-cancel?guid=`

**Parameters:**
- `guid` (optional): Test run GUID to cancel. If not provided, cancels current running test.

**Examples:**
```bash
# Cancel current test run
GET /tests-cancel

# Cancel specific test run
GET /tests-cancel?guid=abc123def456
```

## Usage Examples

### Basic Workflow

```bash
# Check editor status
GET /editor-status

# Trigger compilation
GET /compilation-trigger

# Wait for compilation to complete
GET /compilation-status

# Run all EditMode tests
GET /tests-run-all?mode=EditMode

# Check test status
GET /tests-status
```

### CI/CD Integration

```bash
# 1. Verify Unity is ready
GET /editor-status

# 2. Compile project
GET /compilation-trigger

# 3. Check compilation status
GET /compilation-status

# 4. Run EditMode tests
GET /tests-run-all?mode=EditMode

# 5. Check test results
GET /tests-status

# 6. Run PlayMode tests
GET /tests-run-all?mode=PlayMode

# 7. Check final test results
GET /tests-status
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
- Check `/tests-status` for detailed error information
- Verify test names and patterns are correct

**Compilation Errors:**
- Check `/compilation-status` for error details
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

Status endpoints (`/compilation-status`, `/tests-status`, `/shader-compilation-status`) return progress information when operations are in progress:

**Example `/compilation-status` with progress:**
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
