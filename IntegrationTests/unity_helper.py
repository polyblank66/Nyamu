"""
Unity Helper utilities for working with Unity files and project structure
"""

import os
import shutil
import tempfile
import asyncio
from typing import List, Optional, Dict, Any
from pathlib import Path


class UnityStateManager:
    """
    Manages Unity Editor state to ensure test isolation and proper cleanup
    """

    def __init__(self, mcp_client):
        self.mcp_client = mcp_client

    async def ensure_clean_state(self, cleanup_level="full", skip_force_refresh=False, lightweight=False):
        """
        Ensures Unity is in a clean, working state suitable for tests

        Args:
            cleanup_level: "noop", "minimal", or "full" cleanup level
            skip_force_refresh: If True, skips force refresh but does double regular refresh (legacy)
            lightweight: If True, only does basic refresh and compilation check (legacy)
        """
        try:
            # Handle new cleanup level system
            if cleanup_level == "noop":
                # No-op cleanup for pure protocol tests that never touch Unity
                print("Skipping Unity state cleanup - protocol test only")
                return True

            if cleanup_level == "minimal":
                # Minimal cleanup - no asset refresh, no compilation check, just a tiny wait
                print("Using minimal Unity state cleanup...")
                await self._wait_for_unity_settle(0.1)
                return True

            # Legacy lightweight mode (now equivalent to minimal)
            if lightweight:
                # Lightweight cleanup for tests that don't modify project structure
                print("Using lightweight Unity state cleanup...")
                await self.assets_refresh(force=False)
                await self._wait_for_unity_settle(0.5)
                await self.ensure_compilation_clean()
                await self._wait_for_unity_settle(0.5)
                return True

            if skip_force_refresh:
                # Moderate cleanup - avoids expensive force refresh
                print("Using moderate Unity state cleanup...")
                await self.assets_refresh(force=False)
                await self._wait_for_unity_settle(1.0)
                await self.assets_refresh(force=False)

                compilation_clean = await self.ensure_compilation_clean()
                if not compilation_clean:
                    print("Warning: Unity has compilation errors, trying force refresh...")
                    await self.assets_refresh(force=True)
                    await self._wait_for_unity_settle(1.0)
                    await self.ensure_compilation_clean()

                await self._wait_for_unity_settle(1.0)
                return True

            # Full aggressive cleanup for structural changes (original behavior)
            print("Using full Unity state cleanup...")
            # Force asset refresh to clear any stale references
            await self.assets_refresh(force=True)

            # Double refresh with delay to ensure Unity processes everything
            await self._wait_for_unity_settle(1.0)
            await self.assets_refresh(force=True)

            # Verify compilation succeeds (no errors in codebase)
            compilation_clean = await self.ensure_compilation_clean()

            if not compilation_clean:
                print("Warning: Unity has compilation errors that need to be cleared")
                # Try one more refresh and compilation to clear cache
                await self.assets_refresh(force=True)
                await self._wait_for_unity_settle(2.0)
                await self.ensure_compilation_clean()

            # Give Unity extra time to fully settle and clear all caches
            await self._wait_for_unity_settle(2.0)

            return True

        except Exception as e:
            print(f"Warning: Could not ensure Unity clean state: {e}")
            return False

    async def assets_refresh(self, force=True, max_retries=3):
        """Force Unity asset database refresh"""
        for attempt in range(max_retries):
            try:
                response = await self.mcp_client._send_request("tools/call", {
                    "name": "assets_refresh",
                    "arguments": {"force": force}
                })
                if "result" in response:
                    return True
            except Exception as e:
                if attempt < max_retries - 1:
                    print(f"Asset refresh attempt {attempt + 1} failed, retrying...")
                    import asyncio
                    await asyncio.sleep(1)
                    continue
                else:
                    print(f"Asset refresh failed after {max_retries} attempts: {e}")
                    return False
        return False

    async def ensure_compilation_clean(self, timeout=30):
        """
        Ensures Unity compilation succeeds with no errors
        """
        try:
            response = await self.mcp_client.compilation_trigger(timeout=timeout)
            if "result" in response and "content" in response["result"]:
                content_text = response["result"]["content"][0]["text"]
                # Check if compilation was successful
                if "Compilation completed successfully" in content_text:
                    return True
                elif "Compilation completed with errors" in content_text:
                    print(f"Warning: Unity has compilation errors: {content_text}")
                    return False
            return False
        except Exception as e:
            print(f"Warning: Could not verify compilation state: {e}")
            return False

    async def _wait_for_unity_settle(self, settle_time=2.0):
        """Wait for Unity to process all pending operations"""
        import asyncio
        await asyncio.sleep(settle_time)


class UnityHelper:
    def __init__(self, project_root: str = None, mcp_client = None):
        """
        Initialize Unity Helper

        Args:
            project_root: Unity project root directory
            mcp_client: MCP client instance for calling assets_refresh
        """
        if project_root is None:
            # Default to parent directory of McpTests
            project_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

        self.project_root = project_root
        self.assets_path = os.path.join(project_root, "Assets")
        self.test_module_path = os.path.join(self.assets_path, "TestModule")
        self.backed_up_files = {}
        self.mcp_client = mcp_client

    def backup_file(self, file_path: str) -> str:
        """
        Creates a backup copy of the file

        Args:
            file_path: Path to the file to be backed up

        Returns:
            Path to the backup copy
        """
        if not os.path.exists(file_path):
            raise FileNotFoundError(f"File not found: {file_path}")

        # Create temporary directory for backups
        backup_dir = tempfile.mkdtemp(prefix="unity_test_backup_")
        backup_path = os.path.join(backup_dir, os.path.basename(file_path))

        shutil.copy2(file_path, backup_path)
        self.backed_up_files[file_path] = backup_path

        return backup_path

    def restore_file(self, file_path: str):
        """Restores file from backup copy"""
        if file_path not in self.backed_up_files:
            raise ValueError(f"No backup copy for file: {file_path}")

        backup_path = self.backed_up_files[file_path]
        if os.path.exists(backup_path):
            shutil.copy2(backup_path, file_path)
            # Remove temporary file
            os.remove(backup_path)
            del self.backed_up_files[file_path]

    def restore_all_files(self):
        """Restores all backup copies of files"""
        for file_path in list(self.backed_up_files.keys()):
            try:
                self.restore_file(file_path)
            except Exception as e:
                print(f"Error restoring {file_path}: {e}")

    def create_test_script_with_error(self, file_path: str, error_type: str = "syntax") -> str:
        """
        Creates a test C# script with compilation error

        Args:
            file_path: Path to the file to be created
            error_type: Type of error ('syntax', 'missing_using', 'undefined_var')

        Returns:
            Path to the created file
        """
        class_name = Path(file_path).stem

        if error_type == "syntax":
            content = f'''using UnityEngine;

public class {class_name} : MonoBehaviour
{{
    void Start()
    {{
        // Syntax error - missing semicolon
        Debug.Log("Test error")
        // Another syntax error - missing closing brace

}}'''
        elif error_type == "missing_using":
            content = f'''// Missing using UnityEngine;

public class {class_name} : MonoBehaviour
{{
    void Start()
    {{
        Debug.Log("Test error");
    }}
}}'''
        elif error_type == "undefined_var":
            content = f'''using UnityEngine;

public class {class_name} : MonoBehaviour
{{
    void Start()
    {{
        // Undefined variable
        Debug.Log(undefinedVariable);
    }}
}}'''
        else:
            raise ValueError(f"Unknown error type: {error_type}")

        # Create directory if it doesn't exist
        os.makedirs(os.path.dirname(file_path), exist_ok=True)

        with open(file_path, 'w', encoding='utf-8') as f:
            f.write(content)

        return file_path

    def modify_file_with_error(self, file_path: str, error_type: str = "syntax"):
        """
        Modifies existing file, introducing compilation error

        Args:
            file_path: Path to the file to be modified
            error_type: Type of error to introduce
        """
        # First create backup copy
        self.backup_file(file_path)

        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()

        if error_type == "syntax":
            # Add syntax error to the end of the first method
            if 'void Start()' in content:
                content = content.replace(
                    'void Start()',
                    'void Start()\n    {\n        // Syntax error\n        Debug.Log("Error test")'
                )
            else:
                # If Start method doesn't exist, add it with error
                content = content.replace(
                    '{',
                    '{\n    void Start()\n    {\n        Debug.Log("Error test")\n    }\n',
                    1
                )
        elif error_type == "missing_using":
            # Remove using UnityEngine; and add code that uses UnityEngine
            content = content.replace('using UnityEngine;', '// using UnityEngine; removed')
            # Add method that uses Debug.Log (requires UnityEngine)
            if 'void Start()' not in content:
                content = content.replace(
                    '{',
                    '{\n    void Start()\n    {\n        Debug.Log("This will cause missing using error");\n    }\n',
                    1
                )
        elif error_type == "undefined_var":
            # Add usage of undefined variable
            if 'void Start()' in content:
                content = content.replace(
                    'void Start()',
                    'void Start()\n    {\n        Debug.Log(undefinedVariable);'
                )
            else:
                content = content.replace(
                    '{',
                    '{\n    void Start()\n    {\n        Debug.Log(undefinedVariable);\n    }\n',
                    1
                )

        with open(file_path, 'w', encoding='utf-8') as f:
            f.write(content)

    def get_test_script_path(self) -> str:
        """Returns path to the main test script"""
        return os.path.join(self.assets_path, "TestScript.cs")

    def get_test_module_script_path(self) -> str:
        """Returns path to script in TestModule"""
        return os.path.join(self.test_module_path, "TestModuleScript.cs")

    def get_test_module_asmdef_path(self) -> str:
        """Returns path to TestModule asmdef file"""
        return os.path.join(self.test_module_path, "TestModule.asmdef")

    def create_temp_script_in_assets(self, script_name: str, error_type: str = None) -> str:
        """
        Creates temporary script in Assets folder

        Args:
            script_name: Script name (without extension)
            error_type: Type of error to introduce (if needed)

        Returns:
            Path to the created file
        """
        script_path = os.path.join(self.assets_path, f"{script_name}.cs")

        if error_type:
            return self.create_test_script_with_error(script_path, error_type)
        else:
            # Create correct script
            content = f'''using UnityEngine;

public class {script_name} : MonoBehaviour
{{
    void Start()
    {{
        Debug.Log("{script_name} started");
    }}
}}'''
            with open(script_path, 'w', encoding='utf-8') as f:
                f.write(content)

        return script_path

    def create_temp_script_in_test_module(self, script_name: str, error_type: str = None) -> str:
        """
        Creates temporary script in TestModule folder

        Args:
            script_name: Script name (without extension)
            error_type: Type of error to introduce (if needed)

        Returns:
            Path to the created file
        """
        script_path = os.path.join(self.test_module_path, f"{script_name}.cs")

        if error_type:
            return self.create_test_script_with_error(script_path, error_type)
        else:
            # Create correct script
            content = f'''using UnityEngine;

public class {script_name}
{{
    public void TestMethod()
    {{
        Debug.Log("{script_name} test method called");
    }}
}}'''
            with open(script_path, 'w', encoding='utf-8') as f:
                f.write(content)

        return script_path

    def cleanup_temp_files(self, file_paths: List[str]):
        """Removes temporary files"""
        for file_path in file_paths:
            try:
                if os.path.exists(file_path):
                    os.remove(file_path)
                    # Also remove Unity .meta files
                    meta_path = file_path + ".meta"
                    if os.path.exists(meta_path):
                        os.remove(meta_path)
            except Exception as e:
                print(f"Error removing {file_path}: {e}")

    async def cleanup_temp_files_with_refresh(self, file_paths: List[str]):
        """Removes temporary files/directories and performs force refresh"""
        import shutil

        for path in file_paths:
            try:
                if os.path.isdir(path):
                    # Remove directory
                    shutil.rmtree(path, ignore_errors=True)
                    # Remove Unity .meta file for directory
                    meta_path = path + ".meta"
                    if os.path.exists(meta_path):
                        os.remove(meta_path)
                elif os.path.exists(path):
                    # Remove file
                    os.remove(path)
                    # Also remove Unity .meta files
                    meta_path = path + ".meta"
                    if os.path.exists(meta_path):
                        os.remove(meta_path)
            except Exception as e:
                print(f"Error removing {path}: {e}")

        # Use force refresh after file/directory deletions to ensure Unity detects changes
        await self.assets_refresh_if_available(force=True)

    def wait_for_unity_to_process_files(self):
        """Waits for Unity to process file changes (simple delay)"""
        import time
        time.sleep(2)  # Give Unity time to process files

    async def assets_refresh_if_available(self, force: bool = False, max_retries: int = 10):
        """Refresh Unity assets using MCP client if available

        Args:
            force: Use ImportAssetOptions.ForceUpdate for stronger refresh (recommended for file deletions)
            max_retries: Maximum number of retries if refresh is in progress
        """
        if self.mcp_client:
            for attempt in range(max_retries):
                try:
                    # Call assets_refresh with force flag
                    result = await self.mcp_client.assets_refresh(force=force)

                    # Check if refresh is already in progress
                    if 'result' in result:
                        content = result['result']['content'][0]['text']
                        if 'refresh already in progress' in content.lower():
                            if attempt < max_retries - 1:
                                print(f"Asset refresh in progress, waiting 1s (attempt {attempt + 1}/{max_retries})")
                                await asyncio.sleep(1.0)
                                continue
                            else:
                                # Even on last attempt, wait for in-progress refresh to complete
                                print(f"Asset refresh still in progress after {max_retries} attempts, waiting for completion...")
                                await self._wait_for_mcp_responsive()
                                return

                    # Successful refresh, wait for MCP to be responsive
                    await self._wait_for_mcp_responsive()
                    return

                except Exception as e:
                    if attempt < max_retries - 1:
                        print(f"Warning: Could not refresh assets (attempt {attempt + 1}/{max_retries}): {e}")
                        await asyncio.sleep(1.0)
                        continue
                    else:
                        print(f"Warning: Could not refresh assets after {max_retries} attempts: {e}")
                        # Fallback to regular wait
                        self.wait_for_unity_to_process_files()
                        return
        else:
            # No MCP client available, use regular wait
            self.wait_for_unity_to_process_files()

    async def _wait_for_mcp_responsive(self, max_attempts: int = 10):
        """Wait for MCP server to be responsive after refresh"""
        import asyncio

        for attempt in range(max_attempts):
            try:
                # Try to get tools list as a health check
                await self.mcp_client.list_tools()
                return  # MCP is responsive
            except Exception as e:
                if attempt < max_attempts - 1:
                    print(f"MCP not responsive yet (attempt {attempt + 1}/{max_attempts}), waiting...")
                    await asyncio.sleep(0.5)
                else:
                    print(f"Warning: MCP may not be fully responsive after refresh: {e}")
                    break

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        # Restore all files when exiting context
        self.restore_all_files()