"""
Unity Helper utilities for working with Unity files and project structure
"""

import os
import shutil
import tempfile
import asyncio
import time
import json
import threading
from typing import List, Optional, Dict, Any
from pathlib import Path

# Platform-specific imports for file locking
if os.name == 'nt':  # Windows
    import msvcrt
else:  # Unix/Linux/Mac
    import fcntl


class UnityLockManager:
    """File-based lock for coordinating pytest-xdist workers accessing Unity"""

    # Class-level tracking of lock ownership per process
    _lock_holder = None
    _lock_count = 0
    _lock_file = None  # Shared file handle across all instances in the same process
    _lock_file_path = None
    _thread_lock = threading.Lock()  # Protects critical section within process

    def __init__(self, lock_dir=None, lock_name="unity_state.lock"):
        if lock_dir is None:
            # Use project-local Temp directory to avoid antivirus scanning
            # Find project root (D:\code\Nyamu) by going up from this file
            project_root = Path(__file__).parent.parent
            lock_dir = project_root / "Temp" / "nyamu_unity_locks"

        self.lock_dir = Path(lock_dir)
        self.lock_dir.mkdir(parents=True, exist_ok=True)

        # Create unique lock file path for this instance
        # Use lock_name to differentiate between different Unity projects (for parallel execution)
        self.lock_file_path = self.lock_dir / lock_name

        # Set class-level lock path if not already set (for backward compatibility)
        if UnityLockManager._lock_file_path is None:
            UnityLockManager._lock_file_path = self.lock_file_path

        self.acquired = False

    def __enter__(self):
        """Acquire exclusive lock on Unity state (reentrant within same process)"""
        # Acquire thread lock to make the check-and-lock atomic
        UnityLockManager._thread_lock.acquire()

        try:
            # Check if this process already holds the lock
            if UnityLockManager._lock_count > 0:
                # Reentrant lock - increment counter
                UnityLockManager._lock_count += 1
                self.acquired = False  # We didn't actually acquire the file lock
                return self

            # Acquire the file lock - use instance-specific lock file path
            UnityLockManager._lock_file = open(self.lock_file_path, 'w')

            try:
                if os.name == 'nt':  # Windows
                    msvcrt.locking(UnityLockManager._lock_file.fileno(), msvcrt.LK_LOCK, 1)
                else:  # Unix
                    fcntl.flock(UnityLockManager._lock_file.fileno(), fcntl.LOCK_EX)
            except OSError as e:
                # errno 36 (EDEADLK) means we already have the lock in this process
                # This can happen with async fixture teardowns - treat as reentrant
                if e.errno == 36:
                    UnityLockManager._lock_count += 1
                    self.acquired = False
                    return self
                else:
                    raise

            UnityLockManager._lock_holder = self
            UnityLockManager._lock_count = 1
            self.acquired = True

            return self
        finally:
            # Always release thread lock
            UnityLockManager._thread_lock.release()

    def __exit__(self, exc_type, exc_val, exc_tb):
        """Release lock"""
        # Acquire thread lock to make the decrement-and-unlock atomic
        UnityLockManager._thread_lock.acquire()

        try:
            if not self.acquired:
                # This is a reentrant call, just decrement counter
                UnityLockManager._lock_count -= 1
                return

            # Release the actual file lock
            UnityLockManager._lock_count -= 1
            if UnityLockManager._lock_count == 0:
                UnityLockManager._lock_holder = None
                if UnityLockManager._lock_file:
                    if os.name == 'nt':
                        msvcrt.locking(UnityLockManager._lock_file.fileno(), msvcrt.LK_UNLCK, 1)
                    else:
                        fcntl.flock(UnityLockManager._lock_file.fileno(), fcntl.LOCK_UN)
                    UnityLockManager._lock_file.close()
                    UnityLockManager._lock_file = None
        finally:
            # Always release thread lock
            UnityLockManager._thread_lock.release()


async def wait_for_unity_idle(mcp_client, timeout=30, poll_interval=0.2):
    """Poll Unity status until idle (not compiling/testing/refreshing)"""
    start_time = time.time()

    while time.time() - start_time < timeout:
        try:
            response = await mcp_client.editor_status()

            if "result" in response and "content" in response["result"]:
                status_text = response["result"]["content"][0]["text"]
                status_data = json.loads(status_text)

                is_busy = (
                    status_data.get("isCompiling", False) or
                    status_data.get("isRunningTests", False)
                )

                if not is_busy:
                    return True

        except Exception as e:
            print(f"Unity status check failed: {e}")

        await asyncio.sleep(poll_interval)

    raise TimeoutError(f"Unity did not become idle within {timeout}s")


class UnityStateManager:
    """
    Manages Unity Editor state to ensure test isolation and proper cleanup
    """

    def __init__(self, mcp_client, project_path=None):
        self.mcp_client = mcp_client

        # Create unique lock file name based on project path for parallel execution
        if project_path:
            import hashlib
            # Use hash of project path to create unique but short lock file name
            path_hash = hashlib.md5(str(project_path).encode()).hexdigest()[:8]
            lock_name = f"unity_state_{path_hash}.lock"
        else:
            lock_name = "unity_state.lock"

        self.lock_manager = UnityLockManager(lock_name=lock_name)

    async def ensure_clean_state(self, cleanup_level="full", skip_force_refresh=False, lightweight=False):
        """
        Ensures Unity is in a clean, working state suitable for tests - ALWAYS acquires lock for non-noop cleanup

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

            # CRITICAL: Acquire lock for all Unity operations
            with self.lock_manager:
                # Wait for Unity to be idle before cleanup
                await wait_for_unity_idle(self.mcp_client, timeout=30)

                if cleanup_level == "minimal":
                    # NEW: Minimal now checks for stale file references
                    print("Using minimal Unity state cleanup...")
                    try:
                        status_response = await self.mcp_client.scripts_compile_status()
                        status_text = status_response["result"]["content"][0]["text"]

                        if "cs2001" in status_text.lower() or ("source file" in status_text.lower() and "could not be found" in status_text.lower()):
                            # Stale file references detected - upgrade to force refresh
                            print("Stale file references detected - performing force refresh")
                            await self.assets_refresh(force=True)
                            await wait_for_unity_idle(self.mcp_client, timeout=30)
                        else:
                            # No errors - just brief wait
                            await asyncio.sleep(0.1)
                    except Exception as e:
                        print(f"Minimal cleanup check failed: {e}")
                        await asyncio.sleep(0.1)

                    return True

                # Legacy lightweight mode
                if lightweight:
                    print("Using lightweight Unity state cleanup...")
                    await self.assets_refresh(force=False)
                    await wait_for_unity_idle(self.mcp_client, timeout=30)
                    await self.ensure_compilation_clean()
                    await wait_for_unity_idle(self.mcp_client, timeout=30)
                    return True

                if skip_force_refresh:
                    # Moderate cleanup - avoids expensive force refresh
                    print("Using moderate Unity state cleanup...")
                    await self.assets_refresh(force=False)
                    await wait_for_unity_idle(self.mcp_client, timeout=30)

                    compilation_clean = await self.ensure_compilation_clean()
                    if not compilation_clean:
                        print("Warning: Unity has compilation errors, trying force refresh...")
                        await self.assets_refresh(force=True)
                        await wait_for_unity_idle(self.mcp_client, timeout=30)
                        await self.ensure_compilation_clean()

                    await wait_for_unity_idle(self.mcp_client, timeout=30)
                    return True

                # Full aggressive cleanup for structural changes (optimized)
                print("Using full Unity state cleanup...")
                await self.assets_refresh(force=True)
                await wait_for_unity_idle(self.mcp_client, timeout=30)

                compilation_clean = await self.ensure_compilation_clean()
                await wait_for_unity_idle(self.mcp_client, timeout=30)

                if not compilation_clean:
                    print("Warning: Unity has compilation errors that need to be cleared")
                    await self.assets_refresh(force=True)
                    await wait_for_unity_idle(self.mcp_client, timeout=30)
                    await self.ensure_compilation_clean()
                    await wait_for_unity_idle(self.mcp_client, timeout=30)

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
            response = await self.mcp_client.scripts_compile(timeout=timeout)
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

    async def _wait_for_unity_settle(self, max_wait=2.0, poll=True):
        """
        Wait for Unity to process all pending operations

        Args:
            max_wait: Maximum time to wait in seconds
            poll: If True, poll Unity status and return early when idle
        """
        import asyncio
        import time

        if not poll:
            # Legacy fixed wait
            await asyncio.sleep(max_wait)
            return

        # Poll Unity status with short intervals
        start = time.time()
        while time.time() - start < max_wait:
            try:
                # Check if Unity is busy
                response = await self.mcp_client._send_request("tools/call", {
                    "name": "editor_status",
                    "arguments": {}
                })

                if "result" in response and "content" in response["result"]:
                    import json
                    status_text = response["result"]["content"][0]["text"]
                    status_data = json.loads(status_text)

                    # If Unity is not compiling, return early
                    if not status_data.get("isCompiling", False):
                        return
            except:
                # If status check fails, just wait a bit
                pass

            # Short sleep before next poll
            await asyncio.sleep(0.1)

        # Reached max_wait, return anyway
        return


class UnityHelper:
    def __init__(self, project_root: str = None, mcp_client = None):
        """
        Initialize Unity Helper

        Args:
            project_root: Unity project directory path (can be base dir or Unity project dir)
            mcp_client: MCP client instance for calling assets_refresh
        """
        if project_root is None:
            # Default to parent directory of McpTests
            project_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

        # Check if project_root is already a Unity project directory (contains Assets folder)
        # This supports both old behavior (parent dir) and new behavior (direct project path)
        if os.path.exists(os.path.join(project_root, "Assets")):
            # Already a Unity project directory
            unity_project_path = project_root
        else:
            # Parent directory - append default project name for backward compatibility
            unity_project_path = os.path.join(project_root, "Nyamu.UnityTestProject")

        self.project_root = unity_project_path
        self.assets_path = os.path.join(unity_project_path, "Assets")
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

    async def wait_for_idle_and_refresh(self, force=False, timeout=30):
        """Wait for Unity idle, then refresh, then wait again"""
        await wait_for_unity_idle(self.mcp_client, timeout=timeout)
        await self.assets_refresh_if_available(force=force)
        await wait_for_unity_idle(self.mcp_client, timeout=timeout)

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