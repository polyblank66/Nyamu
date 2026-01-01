"""
MCP Client for interacting with NYAMU MCP Server
"""

import json
import subprocess
import asyncio
import os
import sys
from typing import Dict, Any, Optional

class MCPClient:
    def __init__(self, mcp_server_path: str = None):
        """
        Initialize MCP client

        Args:
            mcp_server_path: Path to MCP server (nyamu.bat file)
        """
        if mcp_server_path is None:
            # Default path to nyamu.bat relative to project root
            project_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
            mcp_server_path = os.path.join(project_root, ".nyamu", "nyamu.bat")

        self.mcp_server_path = mcp_server_path
        self.process = None
        self.request_id = 0

    async def start(self):
        """Start MCP server via nyamu.bat in stdio mode"""
        # Launch nyamu.bat with stdin/stdout pipes (stdio mode)
        self.process = await asyncio.create_subprocess_exec(
            self.mcp_server_path,
            stdin=asyncio.subprocess.PIPE,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE
        )

        # Initialize MCP connection
        await self._send_request("initialize", {"protocolVersion": "2024-11-05"})

    async def stop(self):
        """Stop MCP server"""
        if self.process:
            # On Windows, kill the entire process tree (bat file spawns node.exe)
            if sys.platform == "win32":
                try:
                    subprocess.run(
                        ["taskkill", "/F", "/T", "/PID", str(self.process.pid)],
                        stdout=subprocess.DEVNULL,
                        stderr=subprocess.DEVNULL
                    )
                except:
                    pass
            else:
                self.process.terminate()

            try:
                await asyncio.wait_for(self.process.wait(), timeout=2.0)
            except asyncio.TimeoutError:
                self.process.kill()
                await self.process.wait()

    async def _send_request(self, method: str, params: Dict[str, Any] = None) -> Dict[str, Any]:
        """Send JSON-RPC request to MCP server via stdio"""
        if not self.process:
            raise RuntimeError("MCP server not started")

        self.request_id += 1
        request = {
            "jsonrpc": "2.0",
            "id": self.request_id,
            "method": method
        }

        if params:
            request["params"] = params

        # Send request via stdin
        request_line = json.dumps(request) + "\n"
        self.process.stdin.write(request_line.encode())
        await self.process.stdin.drain()

        # Read response from stdout, skipping progress notifications
        # Progress notifications have 'method' field but no 'id'
        # Actual responses have 'id' field matching our request
        while True:
            response_line = await self.process.stdout.readline()
            response_data = response_line.decode().strip()

            if not response_data:
                raise RuntimeError("Empty response from MCP server")

            response = json.loads(response_data)

            # Check if this is a progress notification (has 'method' but no 'id')
            if "method" in response and "id" not in response:
                # This is a notification, skip it and read next line
                continue

            # Check if this is our response (has 'id' matching our request)
            if "id" in response and response["id"] == self.request_id:
                if "error" in response:
                    raise RuntimeError(f"MCP error: {response['error']}")
                return response

            # Unexpected message format
            raise RuntimeError(f"Unexpected message format: {response}")

    async def _send_unity_request_with_retry(self, method: str, params: Dict[str, Any] = None, max_retries: int = 5,
                                             retry_delay: float = 3.0) -> Dict[str, Any]:
        """Send request to Unity with retry logic for HTTP server restarts

        Unity's HTTP server restarts during compilation/asset refresh, causing -32603 errors.
        This is expected behavior, not a bug. We retry with delays to handle this gracefully.
        """
        for attempt in range(max_retries):
            try:
                return await self._send_request(method, params)
            except RuntimeError as e:
                error_str = str(e)
                # Check for Unity server issues (-32603) that can be retried
                if "-32603" in error_str:
                    # Don't retry timeout errors - they should be passed through
                    if "timeout" in error_str.lower():
                        raise

                    retryable_errors = [
                        "HTTP request failed",  # HTTP server restart during compilation
                        "Test execution failed to start",  # Unity Test Runner initialization issues
                        "Tool execution failed"  # General Unity tool execution issues
                    ]

                    is_retryable = any(error_type in error_str for error_type in retryable_errors)

                    if is_retryable and attempt < max_retries - 1:
                        # Unity is having issues - wait and retry
                        if "HTTP request failed" in error_str:
                            print(f"Unity HTTP server restarting (attempt {attempt + 1}/{max_retries}), waiting {retry_delay}s...")
                        elif "Test execution failed to start" in error_str:
                            print(f"Unity Test Runner initializing (attempt {attempt + 1}/{max_retries}), waiting {retry_delay}s...")
                        else:
                            print(f"Unity tool execution issue (attempt {attempt + 1}/{max_retries}), waiting {retry_delay}s...")

                        import asyncio
                        await asyncio.sleep(retry_delay)
                        continue
                    elif is_retryable:
                        # Max retries exceeded for retryable error
                        if "Test execution failed to start" in error_str:
                            raise RuntimeError(f"Unity Test Runner failed to initialize after {max_retries} attempts. Check Unity Test Runner setup.")
                        else:
                            raise RuntimeError(f"Unity server timeout after {max_retries} attempts. Unity may be stuck in processing.")
                    else:
                        # Non-retryable -32603 error
                        raise
                else:
                    # Different error code - don't retry
                    raise

        # Should not reach here
        raise RuntimeError("Unexpected retry loop exit")

    async def initialize(self) -> Dict[str, Any]:
        """Initialize MCP connection"""
        return await self._send_request("initialize", {"protocolVersion": "2024-11-05"})

    async def list_tools(self) -> Dict[str, Any]:
        """Get list of available tools"""
        return await self._send_request("tools/list")

    async def compilation_trigger(self, timeout: int = 30) -> Dict[str, Any]:
        """Start compilation and wait for completion

        Automatically retries on Unity HTTP server restart (-32603 errors).
        """
        return await self._send_unity_request_with_retry("tools/call", {
            "name": "compilation_trigger",
            "arguments": {"timeout": timeout}
        })

    async def tests_run_single(self, test_name: str, test_mode: str = "EditMode", timeout: int = 60) -> Dict[str, Any]:
        """Run a single specific test by its full name

        Args:
            test_name: Full name of the test to run (e.g., "MyNamespace.MyTests.MySpecificTest")
            test_mode: Test mode: "EditMode" or "PlayMode" (default: "EditMode")
            timeout: Timeout in seconds (default: 60)

        Returns:
            MCP response with test execution results
        """
        return await self._send_unity_request_with_retry("tools/call", {
            "name": "tests_run_single",
            "arguments": {
                "test_name": test_name,
                "test_mode": test_mode,
                "timeout": timeout
            }
        })

    async def tests_run_all(self, test_mode: str = "EditMode", timeout: int = 60) -> Dict[str, Any]:
        """Run all tests in the specified mode

        Args:
            test_mode: Test mode: "EditMode" or "PlayMode" (default: "EditMode")
            timeout: Timeout in seconds (default: 60)

        Returns:
            MCP response with test execution results
        """
        return await self._send_unity_request_with_retry("tools/call", {
            "name": "tests_run_all",
            "arguments": {
                "test_mode": test_mode,
                "timeout": timeout
            }
        })

    async def tests_run_regex(self, test_filter_regex: str = "", test_mode: str = "EditMode", timeout: int = 60) -> Dict[str, Any]:
        """Run tests matching a regex pattern

        Args:
            test_filter_regex: .NET Regex pattern for filtering tests (e.g., ".*PlayerController.*")
            test_mode: Test mode: "EditMode" or "PlayMode" (default: "EditMode")
            timeout: Timeout in seconds (default: 60)

        Returns:
            MCP response with test execution results
        """
        return await self._send_unity_request_with_retry("tools/call", {
            "name": "tests_run_regex",
            "arguments": {
                "test_filter_regex": test_filter_regex,
                "test_mode": test_mode,
                "timeout": timeout
            }
        })

    async def assets_refresh(self, force: bool = False) -> Dict[str, Any]:
        """Refresh Unity asset database

        Args:
            force: Use ImportAssetOptions.ForceUpdate for stronger refresh (recommended for file deletions)
        """
        return await self._send_request("tools/call", {
            "name": "assets_refresh",
            "arguments": {"force": force}
        })

    async def editor_status(self) -> Dict[str, Any]:
        """Get editor status (compilation, testing, play mode)"""
        return await self._send_request("tools/call", {
            "name": "editor_status",
            "arguments": {}
        })

    async def compilation_status(self) -> Dict[str, Any]:
        """Get compilation status without triggering compilation"""
        return await self._send_request("tools/call", {
            "name": "compilation_status",
            "arguments": {}
        })

    async def tests_status(self) -> Dict[str, Any]:
        """Get test execution status without running tests"""
        return await self._send_request("tools/call", {
            "name": "tests_status",
            "arguments": {}
        })

    async def tests_cancel(self, test_run_guid: str = "") -> Dict[str, Any]:
        """Cancel running Unity test execution

        Args:
            test_run_guid: GUID of test run to cancel (optional).
                          If not provided, cancels current running test.

        Note:
            Currently only supports EditMode tests as per Unity's TestRunnerApi limitations.
        """
        return await self._send_request("tools/call", {
            "name": "tests_cancel",
            "arguments": {
                "test_run_guid": test_run_guid
            }
        })

    async def editor_exit_play_mode(self) -> Dict[str, Any]:
        """Exit Unity PlayMode

        Returns:
            MCP response with exit play mode status
        """
        return await self._send_request("tools/call", {
            "name": "editor_exit_play_mode",
            "arguments": {}
        })

    async def compile_shader(self, shader_name: str, timeout: int = 30) -> Dict[str, Any]:
        """Compile a single shader with fuzzy name matching

        Args:
            shader_name: Shader name to search for (supports fuzzy matching)
            timeout: Timeout in seconds (default: 30)

        Returns:
            MCP response with shader compilation results
        """
        return await self._send_request("tools/call", {
            "name": "compile_shader",
            "arguments": {
                "shader_name": shader_name,
                "timeout": timeout
            }
        })

    async def compile_all_shaders(self, timeout: int = 120) -> Dict[str, Any]:
        """Compile all shaders in Unity project

        Args:
            timeout: Timeout in seconds (default: 120)

        Returns:
            MCP response with all shaders compilation results
        """
        return await self._send_request("tools/call", {
            "name": "compile_all_shaders",
            "arguments": {
                "timeout": timeout
            }
        })

    async def compile_shaders_regex(self, pattern: str, timeout: int = 120) -> Dict[str, Any]:
        """Compile shaders matching a regex pattern

        Args:
            pattern: Regex pattern to match against shader file paths
            timeout: Timeout in seconds (default: 120)

        Returns:
            MCP response with regex-matched shaders compilation results
        """
        return await self._send_request("tools/call", {
            "name": "compile_shaders_regex",
            "arguments": {
                "pattern": pattern,
                "timeout": timeout
            }
        })

    async def shader_compilation_status(self) -> Dict[str, Any]:
        """Get current shader compilation status

        Returns:
            MCP response with shader compilation status and last results
        """
        return await self._send_request("tools/call", {
            "name": "shader_compilation_status",
            "arguments": {}
        })

    async def __aenter__(self):
        await self.start()
        return self

    async def __aexit__(self, exc_type, exc_val, exc_tb):
        await self.stop()

def run_sync_mcp_command(method: str, params: Dict[str, Any] = None) -> Dict[str, Any]:
    """Synchronous MCP command call"""
    async def _run():
        async with MCPClient() as client:
            if method == "initialize":
                return await client.initialize()
            elif method == "tools/list":
                return await client.list_tools()
            elif method == "compilation_trigger":
                timeout = (params or {}).get("timeout", 30)
                return await client.compilation_trigger(timeout=timeout)
            elif method == "tests_run_single":
                return await client.tests_run_single(**params)
            elif method == "tests_run_all":
                return await client.tests_run_all(**params)
            elif method == "tests_run_regex":
                return await client.tests_run_regex(**params)
            elif method == "tests_cancel":
                test_run_guid = (params or {}).get("test_run_guid", "")
                return await client.tests_cancel(test_run_guid=test_run_guid)
            else:
                return await client._send_request(method, params)

    return asyncio.run(_run())
