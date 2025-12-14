# Nyamu MCP Server HTTP API Documentation

This document describes the HTTP API provided by the Nyamu MCP Server for interacting with Unity Editor.

## Server Information

- **Base URL:** `http://localhost:17932`
- **Default Port:** `17932` (configurable in Unity: Project Settings → Nyamu MCP Server)
- **Protocol:** HTTP
- **Response Format:** JSON
- **CORS:** Enabled (allows requests from any origin)

## Importing into Postman

1. Open Postman
2. Click **Import** button
3. Select **Upload Files**
4. Choose `NyamuServer-API.postman_collection.json`
5. Click **Import**

The collection includes:
- All 8 API endpoints with descriptions
- Example requests with query parameters
- Sample responses for common scenarios
- Environment variables for base URL and port

## API Endpoints Overview

| Endpoint | Method | Purpose | Parameters |
|----------|--------|---------|------------|
| `/compilation-trigger` | GET | Trigger compilation | None |
| `/compilation-status` | GET | Get compilation status | None |
| `/run-tests` | GET | Execute Unity tests | `mode`, `filter`, `filter_regex` |
| `/test-status` | GET | Get test execution status | None |
| `/refresh-assets` | GET | Refresh asset database | `force` |
| `/editor-status` | GET | Get Editor state | None |
| `/mcp-settings` | GET | Get MCP server settings | None |
| `/cancel-tests` | GET | Cancel running tests | `guid` |

## Endpoint Details

### 1. Compilation Trigger
**Endpoint:** `GET /compilation-trigger`

Triggers Unity script compilation and waits for it to start.

**Response:**
```json
{
    "status": "ok",
    "message": "Compilation started."
}
```

**Use Cases:**
- After modifying C# scripts
- Before running tests to ensure code is compiled
- Part of edit-compile-test workflow

---

### 2. Compilation Status
**Endpoint:** `GET /compilation-status`

Gets current compilation status and errors from the last compilation.

**Response:**
```json
{
    "status": "idle",
    "isCompiling": false,
    "lastCompilationTime": "2025-12-08 12:30:45",
    "lastCompilationRequestTime": "2025-12-08 12:30:40",
    "errors": [
        {
            "file": "Assets/Scripts/Example.cs",
            "line": 42,
            "message": "CS0103: The name 'variableName' does not exist in the current context"
        }
    ]
}
```

**Fields:**
- `status`: "compiling" or "idle"
- `isCompiling`: boolean indicating active compilation
- `lastCompilationTime`: timestamp of last compilation completion
- `lastCompilationRequestTime`: timestamp when compilation was last requested
- `errors`: array of compilation errors (empty if no errors)

**Use Cases:**
- Poll for compilation completion
- Check for compilation errors
- Monitor compilation state during CI/CD

---

### 3. Run Tests
**Endpoint:** `GET /run-tests?mode=EditMode&filter=&filter_regex=`

Starts Unity Test Runner execution with optional filtering.

**Query Parameters:**
- `mode` (optional): Test mode
  - `EditMode` (default): Editor tests
  - `PlayMode`: Runtime tests
- `filter` (optional): Exact test name filter
  - Example: `NyamuTests.PassingTest1`
  - Format: `Namespace.ClassName.TestName`
- `filter_regex` (optional): .NET Regex pattern
  - Example: `Nyamu\.Tests\..*`
  - Matches test names by pattern

**Response:**
```json
{
    "status": "ok",
    "message": "Test execution started."
}
```

**Examples:**
```
# Run all EditMode tests
GET /run-tests?mode=EditMode

# Run all PlayMode tests
GET /run-tests?mode=PlayMode

# Run specific test
GET /run-tests?mode=EditMode&filter=MyNamespace.MyTests.MySpecificTest

# Run tests matching regex pattern
GET /run-tests?mode=PlayMode&filter_regex=Integration\..*
```

**Use Cases:**
- Automated test execution
- CI/CD pipeline integration
- Selective test execution
- Test filtering by namespace or pattern

---

### 4. Test Status
**Endpoint:** `GET /test-status`

Gets current test execution status and results from the last test run.

**Response:**
```json
{
    "status": "idle",
    "isRunning": false,
    "lastTestTime": "2025-12-08 12:35:20",
    "testResults": {
        "totalTests": 5,
        "passedTests": 4,
        "failedTests": 1,
        "skippedTests": 0,
        "duration": 2.345,
        "results": [
            {
                "name": "NyamuTests.PassingTest1",
                "outcome": "Passed",
                "message": "",
                "duration": 0.123
            },
            {
                "name": "NyamuTests.FailingTest",
                "outcome": "Failed",
                "message": "Expected: 5 But was: 3",
                "duration": 0.089
            }
        ]
    },
    "testRunId": "abc123def456",
    "hasError": false,
    "errorMessage": null
}
```

**Fields:**
- `status`: "running" or "idle"
- `isRunning`: boolean indicating active test execution
- `lastTestTime`: timestamp of last test completion
- `testResults`: detailed test statistics and individual results
- `testRunId`: unique GUID for the test run
- `hasError`: whether test execution encountered errors
- `errorMessage`: error description if hasError is true

**Use Cases:**
- Poll for test completion
- Retrieve test results
- Monitor test execution progress
- Parse test failures for reporting

---

### 5. Refresh Assets
**Endpoint:** `GET /refresh-assets?force=false`

Triggers Unity AssetDatabase refresh. Critical for file system changes.

**Query Parameters:**
- `force` (optional): Set to `true` for stronger refresh
  - `false` (default): Normal refresh
  - `true`: Uses `ImportAssetOptions.ForceUpdate`
  - **Required after file deletions** to prevent CS2001 errors

**Response:**
```json
{
    "status": "ok",
    "message": "Asset database refreshed."
}
```

**Recommended Workflow:**
```
1. Make file system changes (create/delete/modify files)
2. Call /refresh-assets (force=true if files were deleted)
3. Wait for Unity to detect changes (monitor via /editor-status)
4. Call /compilation-trigger to compile changes
```

**Examples:**
```
# Normal refresh (after creating new files)
GET /refresh-assets?force=false

# Force refresh (after deleting files)
GET /refresh-assets?force=true
```

**Use Cases:**
- After creating new script files
- After deleting or moving files (use force=true)
- Before compilation to ensure Unity sees changes
- Automated file generation workflows

---

### 6. Editor Status
**Endpoint:** `GET /editor-status`

Gets comprehensive Unity Editor state.

**Response:**
```json
{
    "isCompiling": false,
    "isRunningTests": false,
    "isPlaying": false
}
```

**Fields:**
- `isCompiling`: boolean - script compilation active
- `isRunningTests`: boolean - test execution active
- `isPlaying`: boolean - Play mode active

**Use Cases:**
- Check if Unity is busy before starting operations
- Monitor Editor state during automation
- Ensure Editor is idle before complex operations
- Dashboard/monitoring applications

---

### 7. MCP Settings
**Endpoint:** `GET /mcp-settings`

Gets current Nyamu MCP server settings from Unity.

**Response:**
```json
{
    "responseCharacterLimit": 25000,
    "enableTruncation": true,
    "truncationMessage": "\n\n... (response truncated due to length limit)"
}
```

**Fields:**
- `responseCharacterLimit`: maximum response length
- `enableTruncation`: whether truncation is enabled
- `truncationMessage`: message appended to truncated responses

**Note:** Settings are cached and refreshed every 2 seconds.

**Use Cases:**
- Verify server configuration
- Adjust client behavior based on limits
- Debug truncation issues

---

### 8. Cancel Tests
**Endpoint:** `GET /cancel-tests?guid=`

Cancels running Unity test execution.

**Query Parameters:**
- `guid` (optional): Test run GUID to cancel
  - If not provided: cancels current running test
  - If provided: cancels specific test run by GUID

**Response:**
```json
{
    "status": "ok",
    "message": "Test run cancellation requested for ID: abc123def456",
    "guid": "abc123def456"
}
```

**Limitations:**
- **Only EditMode tests can be cancelled** (Unity API limitation)
- PlayMode test cancellation is not supported

**Examples:**
```
# Cancel current test run
GET /cancel-tests

# Cancel specific test run
GET /cancel-tests?guid=abc123def456
```

**Use Cases:**
- Stop long-running tests
- Cancel accidentally started test runs
- Test timeout handling

---

## Common Workflows

### 1. Edit-Compile-Test Cycle
```
1. Modify C# scripts in your editor/IDE
2. GET /refresh-assets
3. GET /compilation-trigger
4. Poll GET /compilation-status until status="idle"
5. Check errors[] array for compilation errors
6. If no errors: GET /run-tests?mode=EditMode
7. Poll GET /test-status until isRunning=false
8. Parse testResults for pass/fail status
```

### 2. File System Changes
```
# After creating new files:
1. Create files on disk
2. GET /refresh-assets?force=false
3. Wait for Unity to import (check /editor-status)
4. GET /compilation-trigger

# After deleting files:
1. Delete files on disk
2. GET /refresh-assets?force=true  (force=true is critical!)
3. Wait for Unity to process
4. GET /compilation-trigger
```

### 3. Continuous Integration
```
1. GET /editor-status (verify Unity is ready)
2. GET /compilation-trigger
3. Poll /compilation-status (wait for completion)
4. If errors exist: fail build
5. GET /run-tests?mode=EditMode
6. Poll /test-status (wait for completion)
7. GET /run-tests?mode=PlayMode
8. Poll /test-status (wait for completion)
9. Parse results for CI reporting
```

### 4. Selective Test Execution
```
# Run only integration tests:
GET /run-tests?mode=PlayMode&filter_regex=Integration\.Tests\..*

# Run single test:
GET /run-tests?mode=EditMode&filter=MyNamespace.MyTests.SpecificTest

# Run all tests in a namespace:
GET /run-tests?mode=EditMode&filter_regex=MyNamespace\..*
```

## Error Handling

### HTTP Status Codes
- `200 OK`: Successful request
- `404 Not Found`: Invalid endpoint

### Response Status Field
All responses include a `status` field:
- `"ok"`: Operation successful
- `"warning"`: Operation completed with warnings
- `"error"`: Operation failed

### Common Error Scenarios

**1. Server Not Running**
```
Error: Connection refused (ECONNREFUSED)
Solution: Ensure Unity Editor is open with Nyamu project loaded
```

**2. Tests Already Running**
```json
{
    "status": "warning",
    "message": "Tests are already running. Please wait for current test run to complete."
}
```
**Solution:** Poll /test-status and wait for isRunning=false

**3. Asset Refresh In Progress**
```json
{
    "status": "warning",
    "message": "Asset refresh already in progress. Please wait for current refresh to complete."
}
```
**Solution:** Wait and retry

**4. Compilation Timeout**
```json
{
    "status": "warning",
    "message": "Compilation may not have started."
}
```
**Solution:** Check Unity console for issues, verify scripts are valid

## Testing with curl

```bash
# Check compile status
curl http://localhost:17932/compilation-status

# Trigger compilation
curl http://localhost:17932/compilation-trigger

# Run all EditMode tests
curl "http://localhost:17932/run-tests?mode=EditMode"

# Run PlayMode tests with filter
curl "http://localhost:17932/run-tests?mode=PlayMode&filter=MyTests.TestName"

# Get test results
curl http://localhost:17932/test-status

# Refresh assets with force
curl "http://localhost:17932/refresh-assets?force=true"

# Get editor status
curl http://localhost:17932/editor-status

# Cancel current test run
curl http://localhost:17932/cancel-tests
```

## Python Example

```python
import requests
import time

base_url = "http://localhost:17932"

# Trigger compilation
response = requests.get(f"{base_url}/compilation-trigger")
print(response.json())

# Wait for compilation to complete
while True:
    status = requests.get(f"{base_url}/compilation-status").json()
    if status["status"] == "idle":
        if status["errors"]:
            print("Compilation errors:")
            for error in status["errors"]:
                print(f"  {error['file']}:{error['line']} - {error['message']}")
            break
        else:
            print("Compilation successful!")
            break
    time.sleep(0.5)

# Run tests
requests.get(f"{base_url}/run-tests?mode=EditMode")

# Wait for tests to complete
while True:
    status = requests.get(f"{base_url}/test-status").json()
    if not status["isRunning"]:
        results = status["testResults"]
        print(f"Tests completed: {results['passedTests']}/{results['totalTests']} passed")
        if results["failedTests"] > 0:
            print("Failed tests:")
            for result in results["results"]:
                if result["outcome"] == "Failed":
                    print(f"  {result['name']}: {result['message']}")
        break
    time.sleep(1)
```

## JavaScript/Node.js Example

```javascript
const axios = require('axios');

const baseUrl = 'http://localhost:17932';

async function compileAndWait() {
    // Trigger compilation
    await axios.get(`${baseUrl}/compilation-trigger`);

    // Poll for completion
    while (true) {
        const response = await axios.get(`${baseUrl}/compilation-status`);
        const status = response.data;

        if (status.status === 'idle') {
            if (status.errors.length > 0) {
                console.error('Compilation errors:', status.errors);
                return false;
            } else {
                console.log('Compilation successful!');
                return true;
            }
        }

        await new Promise(resolve => setTimeout(resolve, 500));
    }
}

async function runTests(mode = 'EditMode') {
    // Start tests
    await axios.get(`${baseUrl}/run-tests`, {
        params: { mode }
    });

    // Poll for completion
    while (true) {
        const response = await axios.get(`${baseUrl}/test-status`);
        const status = response.data;

        if (!status.isRunning) {
            const results = status.testResults;
            console.log(`Tests completed: ${results.passedTests}/${results.totalTests} passed`);

            if (results.failedTests > 0) {
                console.log('Failed tests:');
                results.results
                    .filter(r => r.outcome === 'Failed')
                    .forEach(r => console.log(`  ${r.name}: ${r.message}`));
            }

            return results.failedTests === 0;
        }

        await new Promise(resolve => setTimeout(resolve, 1000));
    }
}

// Example usage
(async () => {
    const compiled = await compileAndWait();
    if (compiled) {
        await runTests('EditMode');
        await runTests('PlayMode');
    }
})();
```

## Troubleshooting

### Server Not Responding
1. Verify Unity Editor is open
2. Check Unity Console for [NyamuServer] messages
3. Verify port 17932 is not blocked by firewall
4. Check Project Settings → Nyamu MCP Server → Server Port

### Tests Not Running
1. Check /test-status for hasError=true
2. Verify tests exist in Unity Test Runner window
3. Ensure no compilation errors (/compile-status)
4. Check Unity Test Framework is installed

### Compilation Not Starting
1. Check if asset refresh is in progress (/editor-status)
2. Verify scripts have actual changes to compile
3. Check Unity Editor is not frozen/busy
4. Look for Unity console errors

### Asset Refresh Issues
1. Use force=true when deleting files
2. Wait for refresh to complete before compiling
3. Check Unity console for import errors
4. Verify file permissions

## Performance Notes

- **Compilation:** Typically 1-10 seconds depending on project size
- **EditMode Tests:** Usually < 10 seconds for small test suites
- **PlayMode Tests:** 30-120 seconds (includes scene loading and play mode entry)
- **Asset Refresh:** 1-30 seconds depending on number of assets

## Security Considerations

- Server only listens on `localhost` (127.0.0.1)
- No authentication required (local machine only)
- CORS enabled for all origins (development convenience)
- Not intended for production/public exposure

## Important: Understanding MCP Error -32603

When using Nyamu MCP tools, you may encounter **MCP Error -32603** with message "Tool execution failed: HTTP request failed". **This is expected behavior**, not a bug.

### Why This Happens:
- Unity's HTTP server **automatically restarts** during script compilation
- Unity's HTTP server **restarts** during asset database refresh operations
- This prevents interference with Unity's compilation process

### How to Handle:
1. **Expect the error** - It means Unity is compiling/refreshing
2. **Wait 2-5 seconds** - Allow compilation to progress
3. **Retry the command** - The HTTP server will be available again
4. **Repeat as needed** - Until success or reasonable timeout

## Additional Resources

- **Unity Test Framework:** https://docs.unity3d.com/Manual/testing-editortestsrunner.html
- **MCP Protocol:** Model Context Protocol specification
- **Python MCP Tests:** See `McpTests/` directory for pytest integration tests

---

**Version:** 1.0
**Last Updated:** 2025-12-08
**Project:** Nyamu MCP Server
**Package:** dev.polyblank.nyamu
