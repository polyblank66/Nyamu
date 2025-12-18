# Implementing MCP Progress Notifications in Nyamu

This guide explains how to implement MCP (Model Context Protocol) progress notifications for long-running operations in the Nyamu server.

## Table of Contents

1. [Overview](#overview)
2. [MCP Progress Specification](#mcp-progress-specification)
3. [Architecture](#architecture)
4. [Implementation Example: compile_shaders_regex](#implementation-example-compile_shaders_regex)
5. [Step-by-Step Guide](#step-by-step-guide)
6. [Testing](#testing)
7. [Best Practices](#best-practices)
8. [Troubleshooting](#troubleshooting)

## Overview

MCP progress notifications allow servers to send real-time progress updates to clients during long-running operations. This improves user experience by providing feedback instead of making users wait with no information.

**Benefits:**
- Real-time feedback during long operations
- Better user experience
- Follows MCP protocol standard
- Works with all MCP-compatible clients

**Use Cases:**
- Shader compilation (multiple shaders)
- Test execution (multiple tests)
- Asset processing (multiple assets)
- Any operation that processes multiple items sequentially

## MCP Progress Specification

Based on [MCP specification 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/progress):

### Request Format

Clients include a `progressToken` in the request metadata:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "compile_shaders_regex",
    "arguments": { "pattern": ".*Standard.*" },
    "_meta": {
      "progressToken": "unique-token-123"
    }
  }
}
```

### Notification Format

Servers send progress notifications:

```json
{
  "jsonrpc": "2.0",
  "method": "notifications/progress",
  "params": {
    "progressToken": "unique-token-123",
    "progress": 25,
    "total": 100,
    "message": "Compiling Standard.shader (25/100)"
  }
}
```

### Requirements

✅ **MUST**:
- `progressToken` must match the request token
- `progress` must increase with each notification
- Stop sending after operation completes

✅ **MAY**:
- Omit `total` if unknown
- Send notifications at any frequency
- Not send notifications at all

## Architecture

The Nyamu implementation uses a **polling-based architecture** to send progress notifications:

```
┌─────────────┐                 ┌──────────────┐                ┌─────────────┐
│ MCP Client  │                 │  MCP Server  │                │    Unity    │
│ (Claude)    │                 │  (Node.js)   │                │   Editor    │
└──────┬──────┘                 └──────┬───────┘                └──────┬──────┘
       │                               │                               │
       │ tools/call (with             │                               │
       │ progressToken)                │                               │
       ├──────────────────────────────>│                               │
       │                               │                               │
       │                               │ POST /endpoint (async=true)   │
       │                               ├──────────────────────────────>│
       │                               │                               │
       │                               │ HTTP 200 (started)            │
       │                               │<──────────────────────────────┤
       │                               │                               │
       │                               │ Poll /status every 500ms      │
       │                               ├──────────────────────────────>│
       │                               │                               │
       │                               │ Status + progress             │
       │                               │<──────────────────────────────┤
       │                               │                               │
       │ notifications/progress        │                               │
       │<──────────────────────────────┤                               │
       │                               │                               │
       │                               │ Continue polling...           │
       │                               ├──────────────────────────────>│
       │                               │                               │
       │ notifications/progress        │                               │
       │<──────────────────────────────┤                               │
       │                               │                               │
       │                               │ Status: complete              │
       │                               │<──────────────────────────────┤
       │                               │                               │
       │ tools/call result             │                               │
       │<──────────────────────────────┤                               │
       │                               │                               │
```

### Key Design Decisions

1. **Polling instead of Push**: Unity doesn't need to make HTTP calls back to the MCP server
2. **Async mode parameter**: Unity returns immediately when `async=true` is set
3. **Status polling**: MCP server polls Unity's status endpoint to get progress
4. **Existing patterns**: Reuses thread-safe state management and polling infrastructure

## Implementation Example: compile_shaders_regex

This section walks through the actual implementation of progress notifications for shader regex compilation.

### Unity C# Backend Changes

#### 1. Add Async Parameter to Request DTO

```csharp
[Serializable]
public class CompileShadersRegexRequest
{
    public string pattern;
    public bool async;  // NEW: if true, return immediately after queuing
}
```

#### 2. Create Progress Info DTO

```csharp
[Serializable]
public class ShaderRegexProgressInfo
{
    public string pattern;
    public int totalShaders;
    public int completedShaders;
    public string currentShader;
}
```

#### 3. Add Progress Tracking State

```csharp
// Regex shader compilation progress tracking
static string _regexShadersPattern = "";
static int _regexShadersTotal = 0;
static int _regexShadersCompleted = 0;
static string _regexShadersCurrentShader = "";
```

Protected by existing `_shaderCompilationResultLock`.

#### 4. Update Status Response DTO

```csharp
[Serializable]
public class ShaderCompilationStatusResponse<T>
{
    public string status;
    public bool isCompiling;
    public string lastCompilationType;
    public string lastCompilationTime;
    public T lastCompilationResult;
    public ShaderRegexProgressInfo progress;  // NEW
}
```

#### 5. Modify Handler to Support Async Mode

```csharp
static string HandleCompileShadersRegexRequest(HttpListenerRequest request)
{
    // ... validation ...

    // Check if async mode is requested
    if (requestData.async)
    {
        // Async mode: queue compilation and return immediately
        lock (_mainThreadActionQueue)
        {
            _mainThreadActionQueue.Enqueue(() =>
            {
                var result = CompileShadersRegex(requestData.pattern);
                // ... store result ...
            });
        }

        return "{\"status\":\"ok\",\"message\":\"Shader compilation started.\"}";
    }

    // Blocking mode: wait for compilation (existing behavior)
    // ...
}
```

#### 6. Track Progress During Compilation

```csharp
static CompileShadersRegexResponse CompileShadersRegex(string pattern)
{
    // ... find matching shaders ...

    // Initialize progress tracking
    lock (_shaderCompilationResultLock)
    {
        _regexShadersPattern = pattern;
        _regexShadersTotal = matchingShaders.Count;
        _regexShadersCompleted = 0;
        _regexShadersCurrentShader = "";
    }

    for (var i = 0; i < matchingShaders.Count; i++)
    {
        var shaderPath = matchingShaders[i];

        // Update progress tracking
        lock (_shaderCompilationResultLock)
        {
            _regexShadersCompleted = i;
            _regexShadersCurrentShader = shaderPath;
        }

        var result = CompileShaderAtPath(shaderPath);
        results.Add(result);
    }

    // Mark progress as complete
    lock (_shaderCompilationResultLock)
    {
        _regexShadersCompleted = matchingShaders.Count;
        _regexShadersCurrentShader = "";
    }

    return response;
}
```

#### 7. Include Progress in Status Endpoint

```csharp
static string HandleShaderCompilationStatusRequest(HttpListenerRequest request)
{
    // ... get status ...

    if (regexResult != null)
    {
        var response = new ShaderCompilationStatusResponse<CompileShadersRegexResponse>
        {
            status = status,
            isCompiling = _isCompilingShaders,
            lastCompilationType = typeCopy,
            lastCompilationTime = timeString,
            lastCompilationResult = regexResult
        };

        // Add progress info if currently compiling
        if (_isCompilingShaders && typeCopy == "regex")
        {
            lock (_shaderCompilationResultLock)
            {
                response.progress = new ShaderRegexProgressInfo
                {
                    pattern = _regexShadersPattern,
                    totalShaders = _regexShadersTotal,
                    completedShaders = _regexShadersCompleted,
                    currentShader = _regexShadersCurrentShader
                };
            }
        }

        return JsonUtility.ToJson(response);
    }
}
```

### MCP Server Node.js Changes

#### 1. Add Notification Method

```javascript
sendProgressNotification(progressToken, progress, total, message) {
    const notification = {
        jsonrpc: '2.0',
        method: 'notifications/progress',
        params: {
            progressToken: progressToken,
            progress: progress,
            total: total
        }
    };

    if (message) {
        notification.params.message = message;
    }

    // Send notification using existing sendJsonResponse method
    this.sendJsonResponse(notification, this.activeProtocol);
}
```

#### 2. Extract progressToken from Request

```javascript
async handleToolCall(params, id) {
    const { name, arguments: args, _meta } = params;
    const progressToken = _meta?.progressToken || null;

    try {
        switch (name) {
            case 'compile_shaders_regex':
                return await this.callCompileShadersRegex(
                    id,
                    args.pattern,
                    args.timeout || 120,
                    progressToken
                );
            // ... other cases ...
        }
    } catch (error) {
        throw error;
    }
}
```

#### 3. Route Based on progressToken

```javascript
async callCompileShadersRegex(id, pattern, timeoutSeconds, progressToken) {
    if (progressToken) {
        // Asynchronous mode with progress notifications
        return await this.callCompileShadersRegexWithProgress(
            id, pattern, timeoutSeconds, progressToken
        );
    } else {
        // Original blocking mode (backward compatibility)
        return await this.callCompileShadersRegexBlocking(
            id, pattern, timeoutSeconds
        );
    }
}
```

#### 4. Implement Blocking Mode (Backward Compatibility)

```javascript
async callCompileShadersRegexBlocking(id, pattern, timeoutSeconds) {
    await this.ensureResponseFormatter();

    const timeoutMs = timeoutSeconds * 1000;
    const requestBody = JSON.stringify({ pattern });
    const response = await this.makeHttpPostRequest(
        '/compile-shaders-regex',
        requestBody,
        timeoutMs
    );

    const formattedText = this.formatCompileShadersRegexResponse(response);
    const finalText = this.responseFormatter.formatResponse(formattedText);

    return {
        jsonrpc: '2.0',
        id,
        result: { content: [{ type: 'text', text: finalText }] }
    };
}
```

#### 5. Implement Progress Mode

```javascript
async callCompileShadersRegexWithProgress(id, pattern, timeoutSeconds, progressToken) {
    await this.ensureResponseFormatter();

    const timeoutMs = timeoutSeconds * 1000;
    const startTime = Date.now();

    // 1. Start compilation asynchronously
    const startBody = JSON.stringify({ pattern, async: true });
    await this.makeHttpPostRequest('/compile-shaders-regex', startBody);

    // 2. Poll for progress
    let lastProgress = -1;
    while (Date.now() - startTime < timeoutMs) {
        try {
            const statusResponse = await this.makeHttpRequest('/shader-compilation-status');
            const status = JSON.parse(statusResponse);

            // Check if compilation complete
            if (!status.isCompiling && status.lastCompilationType === 'regex') {
                // Return final result
                const formatted = this.formatCompileShadersRegexResponse(
                    status.lastCompilationResult
                );
                const finalText = this.responseFormatter.formatResponse(formatted);
                return {
                    jsonrpc: '2.0',
                    id,
                    result: { content: [{ type: 'text', text: finalText }] }
                };
            }

            // Send progress notification if progress changed
            if (status.isCompiling && status.progress) {
                const currentProgress = status.progress.completedShaders;
                if (currentProgress > lastProgress) {
                    const currentShaderName = status.progress.currentShader ?
                        status.progress.currentShader.split('/').pop() : '';
                    this.sendProgressNotification(
                        progressToken,
                        currentProgress,
                        status.progress.totalShaders,
                        `Compiling ${currentShaderName} (${currentProgress}/${status.progress.totalShaders})`
                    );
                    lastProgress = currentProgress;
                }
            }

            // Wait before next poll
            await new Promise(resolve => setTimeout(resolve, 500));

        } catch (pollError) {
            // Handle Unity restart errors
            if (this.isUnityRestartingError(pollError)) {
                await new Promise(resolve => setTimeout(resolve, 2000));
                continue;
            }
            throw pollError;
        }
    }

    // Timeout
    throw new Error(`Shader compilation timed out after ${timeoutSeconds} seconds`);
}
```

#### 6. Update Protocol Version

**CRITICAL**: Update the MCP protocol version to advertise progress support:

```javascript
case 'initialize':
    return {
        jsonrpc: '2.0',
        id,
        result: {
            protocolVersion: '2025-11-25',  // Updated from '2024-11-05'
            capabilities: this.capabilities,
            serverInfo: {
                name: 'NyamuServer',
                version: '1.0.0'
            }
        }
    };
```

## Step-by-Step Guide

Follow these steps to add progress notification support to any long-running operation:

### Phase 1: Unity C# Backend

**Step 1: Add async parameter to request DTO**
- Add `public bool async;` field to your request class
- This enables clients to request async mode

**Step 2: Create progress info DTO**
- Create a `[Serializable]` class to hold progress information
- Include: total items, completed items, current item name/path

**Step 3: Add progress tracking state variables**
- Add static fields to track: total, completed, current item
- Protect with existing locks

**Step 4: Add progress field to status response**
- Add progress info field to your status response DTO
- Make it optional (only populated during operation)

**Step 5: Modify HTTP handler for async mode**
```csharp
if (requestData.async)
{
    // Queue work and return immediately
    lock (_mainThreadActionQueue)
    {
        _mainThreadActionQueue.Enqueue(() => {
            // Do work
            // Update result
            // Release lock
        });
    }
    return "{\"status\":\"ok\",\"message\":\"Operation started.\"}";
}
// else: existing blocking behavior
```

**Step 6: Track progress during operation**
```csharp
// Before loop
lock (_resultLock)
{
    _total = items.Count;
    _completed = 0;
    _current = "";
}

// In loop
for (var i = 0; i < items.Count; i++)
{
    lock (_resultLock)
    {
        _completed = i;
        _current = items[i];
    }

    // Process item
}

// After loop
lock (_resultLock)
{
    _completed = items.Count;
    _current = "";
}
```

**Step 7: Include progress in status endpoint**
```csharp
if (isOperating && operationType == "your_type")
{
    lock (_resultLock)
    {
        response.progress = new YourProgressInfo
        {
            total = _total,
            completed = _completed,
            current = _current
        };
    }
}
```

### Phase 2: MCP Server Node.js

**Step 1: Ensure sendProgressNotification exists**
- Add the notification method if not already present
- Use `sendJsonResponse()` to send notifications

**Step 2: Extract progressToken**
```javascript
async handleToolCall(params, id) {
    const { name, arguments: args, _meta } = params;
    const progressToken = _meta?.progressToken || null;

    // Pass to handler
}
```

**Step 3: Create routing function**
```javascript
async callYourOperation(id, ...args, progressToken) {
    if (progressToken) {
        return await this.callYourOperationWithProgress(id, ...args, progressToken);
    } else {
        return await this.callYourOperationBlocking(id, ...args);
    }
}
```

**Step 4: Keep blocking version**
```javascript
async callYourOperationBlocking(id, ...args) {
    // Existing implementation
    // Make blocking HTTP request
    // Return result
}
```

**Step 5: Implement progress version**
```javascript
async callYourOperationWithProgress(id, ...args, progressToken) {
    // 1. Start operation asynchronously
    const body = JSON.stringify({ ...params, async: true });
    await this.makeHttpPostRequest('/your-endpoint', body);

    // 2. Poll for progress
    let lastProgress = -1;
    while (/* not timeout */) {
        const status = await this.makeHttpRequest('/your-status-endpoint');
        const parsed = JSON.parse(status);

        // Check if complete
        if (!parsed.isOperating && parsed.lastOperationType === 'your_type') {
            // Return final result
            return /* formatted result */;
        }

        // Send progress if changed
        if (parsed.isOperating && parsed.progress) {
            const current = parsed.progress.completed;
            if (current > lastProgress) {
                this.sendProgressNotification(
                    progressToken,
                    current,
                    parsed.progress.total,
                    `Processing ${parsed.progress.current} (${current}/${parsed.progress.total})`
                );
                lastProgress = current;
            }
        }

        await new Promise(resolve => setTimeout(resolve, 500));
    }
}
```

**Step 6: Update tool description**
```javascript
your_tool: {
    description: "... Supports MCP progress notifications when progressToken provided in _meta. Progress updates sent every ~500ms.",
    inputSchema: { /* ... */ }
}
```

### Phase 3: Testing & Documentation

**Step 1: Test without progressToken (backward compatibility)**
```javascript
// Should work exactly as before
await client.callTool('your_tool', { /* args */ });
```

**Step 2: Test with progressToken**
```javascript
// Should receive progress notifications
await client.callTool('your_tool', { /* args */ }, { progressToken: 'test-123' });
```

**Step 3: Update AGENT-GUIDE.md**
```markdown
### Your Operation
- Supports MCP progress notifications
- Progress updates sent every ~500ms
- Example: see progress during long operations
```

**Step 4: Verify protocol version**
```javascript
// Ensure server advertises 2025-11-25
protocolVersion: '2025-11-25'
```

## Testing

### Manual Testing with Python

Create a test script to verify progress notifications:

```python
import asyncio
import json
from mcp_client import MCPClient

async def test_progress():
    client = MCPClient()
    await client.start()

    # Track received notifications
    notifications = []

    def on_notification(notif):
        if notif['method'] == 'notifications/progress':
            notifications.append(notif['params'])
            print(f"Progress: {notif['params']['progress']}/{notif['params']['total']} - {notif['params']['message']}")

    client.on_notification = on_notification

    # Call tool with progressToken
    response = await client.call_tool(
        'compile_shaders_regex',
        {'pattern': '.*TestShaders.*'},
        progress_token='test-123'
    )

    print(f"\nReceived {len(notifications)} progress notifications")
    print(f"Final result: {response}")

    await client.stop()

asyncio.run(test_progress())
```

### Automated Testing

Add tests to `McpTests/`:

```python
@pytest.mark.asyncio
async def test_progress_notifications_sent(mcp_client):
    """Test that progress notifications are sent with progressToken"""
    notifications = []

    def capture(notif):
        if notif['method'] == 'notifications/progress':
            notifications.append(notif)

    mcp_client.on_notification = capture

    response = await mcp_client.compile_shaders_regex(
        pattern='.*TestShaders.*',
        progress_token='test-123'
    )

    assert len(notifications) > 0
    assert all(n['params']['progressToken'] == 'test-123' for n in notifications)
    assert all(n['params']['progress'] <= n['params']['total'] for n in notifications)
```

### Testing Checklist

- [ ] Works without progressToken (backward compatibility)
- [ ] Works with progressToken (sends notifications)
- [ ] Progress values increase monotonically
- [ ] Total value remains consistent
- [ ] Message field is human-readable
- [ ] Final result is correct
- [ ] Handles Unity restart during operation
- [ ] Handles timeout correctly
- [ ] No memory leaks or race conditions

## Best Practices

### Do's ✅

1. **Always maintain backward compatibility**
   - Check for progressToken presence
   - Fall back to blocking mode if not provided

2. **Use thread-safe state management**
   - Protect progress state with locks
   - Use existing lock objects when possible

3. **Send meaningful progress messages**
   ```javascript
   `Compiling StandardShader.shader (25/100)`
   `Running test MyTest.cs (10/50)`
   `Processing texture_001.png (300/1000)`
   ```

4. **Poll at reasonable intervals**
   - 500ms is recommended
   - Adjust based on operation duration
   - Don't poll faster than 100ms

5. **Only send when progress changes**
   ```javascript
   if (currentProgress > lastProgress) {
       sendProgressNotification(...);
       lastProgress = currentProgress;
   }
   ```

6. **Clear progress state after completion**
   ```csharp
   lock (_resultLock) {
       _completed = _total;
       _current = "";
   }
   ```

7. **Handle Unity restart gracefully**
   ```javascript
   catch (pollError) {
       if (this.isUnityRestartingError(pollError)) {
           await new Promise(resolve => setTimeout(resolve, 2000));
           continue;
       }
       throw pollError;
   }
   ```

### Don'ts ❌

1. **Don't break existing API**
   - Always support non-async mode
   - Don't require progressToken

2. **Don't send notifications too frequently**
   - Avoid < 100ms intervals
   - Rate limit if needed

3. **Don't forget to advertise protocol version**
   ```javascript
   // ❌ Wrong
   protocolVersion: '2024-11-05'

   // ✅ Correct
   protocolVersion: '2025-11-25'
   ```

4. **Don't send notifications after completion**
   - Stop polling when operation finishes
   - Clear progress state

5. **Don't use progress for error reporting**
   - Progress is for status updates
   - Use error responses for errors

6. **Don't make total value change**
   ```javascript
   // ❌ Wrong - total changes
   progress: 10, total: 50
   progress: 20, total: 100  // Changed!

   // ✅ Correct - total consistent
   progress: 10, total: 100
   progress: 20, total: 100
   ```

## Troubleshooting

### Progress Notifications Not Received

**Problem**: Client doesn't receive progress notifications

**Solutions**:
1. Check protocol version is `2025-11-25`
2. Verify client supports progress (restart client)
3. Confirm progressToken is being passed
4. Check Unity HTTP server is responsive
5. Verify notifications are being sent (add logging)

### Progress Stuck at Old Value

**Problem**: Progress stops updating mid-operation

**Solutions**:
1. Check Unity is still processing (not crashed)
2. Verify progress state is being updated in loop
3. Ensure locks aren't causing deadlock
4. Check polling interval isn't too slow

### Multiple Progress Notifications with Same Value

**Problem**: Duplicate progress notifications sent

**Solutions**:
1. Only send when `currentProgress > lastProgress`
2. Track last sent progress value
3. Consider debouncing logic

### Progress Goes Backwards

**Problem**: Progress value decreases

**Solutions**:
1. Ensure progress is monotonically increasing
2. Don't reset progress during operation
3. Initialize progress before operation starts

### Timeout Before Completion

**Problem**: Operation times out but Unity is still processing

**Solutions**:
1. Increase timeout value
2. Send progress more frequently to keep connection alive
3. Check Unity performance issues

## Summary

MCP progress notifications provide real-time feedback during long-running operations. The key implementation points are:

1. **Unity C#**: Add async mode, track progress, expose via status endpoint
2. **MCP Server**: Extract progressToken, poll status, send notifications
3. **Protocol**: Advertise version `2025-11-25`
4. **Testing**: Verify with and without progressToken
5. **Backward Compatibility**: Always support non-async mode

The polling-based architecture is simple, reliable, and consistent with existing Nyamu patterns.

## References

- [MCP Specification 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/progress)
- Nyamu Implementation: `compile_shaders_regex` (commit: 7f16eb8)
- [AGENT-GUIDE.md](../Packages/dev.polyblank.nyamu/AGENT-GUIDE.md)
- [NyamuServer-API-Guide.md](../Packages/dev.polyblank.nyamu/NyamuServer-API-Guide.md)
