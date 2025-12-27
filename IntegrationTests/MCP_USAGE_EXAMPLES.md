# YAMU MCP Server Usage Examples

This document provides practical examples of using the YAMU MCP server tools for common Unity development workflows.

## Quick Reference

### File Operations Workflow
```
File Changes → refresh_assets (force=true for deletions) → Wait for MCP → compile_and_wait
```

### Tool Overview
- `refresh_assets`: Updates Unity's file tracking (CRITICAL for file operations)
- `compile_and_wait`: Triggers Unity compilation and reports errors
- `run_tests`: Executes Unity Test Runner tests

## Common Usage Patterns

### 1. Create New File and Compile
```python
import asyncio
from mcp_client import MCPClient

async def create_and_compile():
    async with MCPClient() as client:
        # 1. Create your C# file
        with open("Assets/MyScript.cs", "w") as f:
            f.write('''using UnityEngine;
public class MyScript : MonoBehaviour
{
    void Start() { Debug.Log("Hello World"); }
}''')

        # 2. Refresh assets (regular refresh for new files)
        await client.refresh_assets(force=False)

        # 3. Compile
        result = await client.compile_and_wait(timeout=30)
        print(result["result"]["content"][0]["text"])
```

### 2. Delete File and Compile
```python
async def delete_and_compile():
    async with MCPClient() as client:
        # 1. Delete the file
        import os
        if os.path.exists("Assets/MyScript.cs"):
            os.remove("Assets/MyScript.cs")
            # Also remove Unity meta file
            os.remove("Assets/MyScript.cs.meta")

        # 2. Force refresh (CRITICAL for deletions to prevent CS2001 errors)
        await client.refresh_assets(force=True)

        # 3. Compile
        result = await client.compile_and_wait(timeout=30)
        print(result["result"]["content"][0]["text"])
```

### 3. Modify Existing File and Compile
```python
async def modify_and_compile():
    async with MCPClient() as client:
        # 1. Modify existing file (no refresh needed for content changes)
        with open("Assets/TestScript.cs", "r") as f:
            content = f.read()

        # Add syntax error for testing
        modified_content = content.replace(";", "")  # Remove semicolons

        with open("Assets/TestScript.cs", "w") as f:
            f.write(modified_content)

        # 2. Compile (no refresh needed for file content changes)
        result = await client.compile_and_wait(timeout=30)
        print(result["result"]["content"][0]["text"])
```

### 4. Run Tests After Changes
```python
async def test_workflow():
    async with MCPClient() as client:
        # 1. Make changes and refresh if needed
        # ... file operations ...

        # 2. Compile first
        compile_result = await client.compile_and_wait(timeout=30)

        # 3. Only run tests if compilation succeeded
        if "successfully" in compile_result["result"]["content"][0]["text"]:
            test_result = await client.run_tests(
                test_mode="PlayMode",
                test_filter="",  # Run all tests
                timeout=60
            )
            print(test_result["result"]["content"][0]["text"])
        else:
            print("Compilation failed, skipping tests")
```

### 5. Batch File Operations
```python
async def batch_operations():
    async with MCPClient() as client:
        # 1. Create multiple files
        files = ["Script1.cs", "Script2.cs", "Script3.cs"]
        for filename in files:
            with open(f"Assets/{filename}", "w") as f:
                f.write(f'''using UnityEngine;
public class {filename[:-3]} : MonoBehaviour
{{
    void Start() {{ Debug.Log("{filename[:-3]} started"); }}
}}''')

        # 2. Single refresh for all new files
        await client.refresh_assets(force=False)

        # 3. Compile all at once
        result = await client.compile_and_wait(timeout=45)
        print(result["result"]["content"][0]["text"])
```

### 6. Test Different Test Modes
```python
async def run_all_tests():
    async with MCPClient() as client:
        # Run EditMode tests (fast, editor-only)
        edit_result = await client.run_tests(
            test_mode="EditMode",
            timeout=30
        )
        print("EditMode Results:")
        print(edit_result["result"]["content"][0]["text"])

        # Run PlayMode tests (slower, runtime)
        play_result = await client.run_tests(
            test_mode="PlayMode",
            timeout=60
        )
        print("PlayMode Results:")
        print(play_result["result"]["content"][0]["text"])
```

### 7. Error Handling
```python
async def robust_compilation():
    async with MCPClient() as client:
        try:
            # Attempt compilation
            result = await client.compile_and_wait(timeout=30)
            content = result["result"]["content"][0]["text"]

            if "successfully" in content:
                print("✅ Compilation successful")
            elif "errors:" in content:
                print("❌ Compilation failed with errors:")
                print(content)

        except Exception as e:
            print(f"💥 MCP Error: {e}")
            # Try force refresh and retry
            await client.refresh_assets(force=True)
            result = await client.compile_and_wait(timeout=30)
            print("Retry result:", result["result"]["content"][0]["text"])
```

### 8. Directory Operations
```python
async def directory_operations():
    async with MCPClient() as client:
        import os
        import shutil

        # 1. Create directory with scripts
        os.makedirs("Assets/MyModule", exist_ok=True)
        with open("Assets/MyModule/ModuleScript.cs", "w") as f:
            f.write('''using UnityEngine;
public class ModuleScript
{
    public void DoSomething() { Debug.Log("Module working"); }
}''')

        # 2. Refresh for new directory and file
        await client.refresh_assets(force=False)

        # 3. Compile
        result = await client.compile_and_wait()
        print("After creation:", result["result"]["content"][0]["text"])

        # 4. Remove directory
        shutil.rmtree("Assets/MyModule")
        # Remove Unity meta file
        if os.path.exists("Assets/MyModule.meta"):
            os.remove("Assets/MyModule.meta")

        # 5. Force refresh for directory deletion
        await client.refresh_assets(force=True)

        # 6. Compile again
        result = await client.compile_and_wait()
        print("After deletion:", result["result"]["content"][0]["text"])
```

## Best Practices

### ✅ DO
- Always call `refresh_assets(force=True)` after deleting files/directories
- Use `refresh_assets(force=False)` after creating new files
- Wait for MCP responsiveness after refresh before compiling
- Handle compilation errors gracefully
- Use appropriate timeouts for large projects
- Clean up test files properly in automated environments

### ❌ DON'T
- Skip refresh after file system changes - Unity won't detect them
- Use regular refresh after deletions - causes CS2001 errors
- Forget to remove Unity .meta files when deleting files
- Run tests before ensuring compilation succeeds
- Use very short timeouts for complex operations

## Troubleshooting

### CS2001 "Source file could not be found"
```python
# Fix with force refresh
await client.refresh_assets(force=True)
await client.compile_and_wait()
```

### MCP Server Not Responding
```python
# Check basic connectivity
try:
    tools = await client.list_tools()
    print("MCP server is responsive")
except Exception as e:
    print(f"MCP server issue: {e}")
```

### Compilation Hanging
```python
# Use shorter timeout and force refresh
await client.refresh_assets(force=True)
result = await client.compile_and_wait(timeout=15)
```

## Integration with Unity Helper (Test Environment)

```python
# Using the unity_helper for test scenarios
from unity_helper import UnityHelper

async def test_scenario():
    unity_helper = UnityHelper(mcp_client=client)

    # Create test file
    test_file = unity_helper.create_temp_script_in_assets("TestScript", "syntax")

    # Refresh and compile
    await unity_helper.refresh_assets_if_available()
    result = await client.compile_and_wait()

    # Cleanup with force refresh
    await unity_helper.cleanup_temp_files_with_refresh([test_file])
```

This ensures proper cleanup and prevents Unity file tracking issues in test environments.