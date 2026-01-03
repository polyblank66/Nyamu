"""
Unity Instance Manager for Parallel Test Execution

Manages Unity Editor batch-mode lifecycle for pytest-xdist workers.
"""

import asyncio
import os
import subprocess
import time
import json
import hashlib
from pathlib import Path
from typing import Optional
import requests
import filelock


def get_unity_version_from_project(project_path: Path) -> str:
    """
    Read Unity version from ProjectVersion.txt.

    Args:
        project_path: Path to Unity project root

    Returns:
        Unity version string (e.g., "2021.3.45f2")

    Raises:
        FileNotFoundError: If ProjectVersion.txt is missing
        ValueError: If version cannot be parsed
    """
    version_file = project_path / "ProjectSettings" / "ProjectVersion.txt"

    if not version_file.exists():
        raise FileNotFoundError(f"ProjectVersion.txt not found at {version_file}")

    with open(version_file, 'r') as f:
        for line in f:
            if line.startswith("m_EditorVersion:"):
                version = line.split(":", 1)[1].strip()
                if version:
                    return version

    raise ValueError(f"Could not parse Unity version from {version_file}")


def find_unity_exe_for_version(unity_version: str) -> Optional[str]:
    """
    Find Unity.exe for a specific version using Unity Hub locations.

    Search strategy (matching unity-merge.bat):
    1. Unity Hub secondaryInstallPath.json (custom installation path)
    2. Standard Unity Hub path (%ProgramFiles%\\Unity\\Hub\\Editor)
    3. Alternate standard path (C:\\Program Files\\Unity\\Hub\\Editor)
    4. Search common drives (C, D, E, S)

    Args:
        unity_version: Unity version to find (e.g., "2021.3.45f2")

    Returns:
        Path to Unity.exe if found, None otherwise
    """
    # Method 1: Check Unity Hub's secondaryInstallPath.json for custom installation
    appdata = os.environ.get("APPDATA")
    if appdata:
        secondary_path_file = Path(appdata) / "UnityHub" / "secondaryInstallPath.json"
        if secondary_path_file.exists():
            try:
                with open(secondary_path_file, 'r') as f:
                    content = f.read().strip()
                    # Remove quotes from JSON string (it's just a quoted path)
                    secondary_path = content.strip('"')
                    unity_exe = Path(secondary_path) / unity_version / "Editor" / "Unity.exe"
                    if unity_exe.exists():
                        print(f"Found Unity {unity_version} at secondary install path: {unity_exe}")
                        return str(unity_exe)
            except (json.JSONDecodeError, OSError):
                pass  # Continue to other methods

    # Method 2: Check standard Unity Hub path
    program_files = os.environ.get("ProgramFiles", "C:\\Program Files")
    standard_path = Path(program_files) / "Unity" / "Hub" / "Editor"
    unity_exe = standard_path / unity_version / "Editor" / "Unity.exe"
    if unity_exe.exists():
        print(f"Found Unity {unity_version} at standard path: {unity_exe}")
        return str(unity_exe)

    # Method 3: Check alternate standard path (in case ProgramFiles is different)
    alt_path = Path("C:\\Program Files\\Unity\\Hub\\Editor")
    unity_exe = alt_path / unity_version / "Editor" / "Unity.exe"
    if unity_exe.exists():
        print(f"Found Unity {unity_version} at alternate standard path: {unity_exe}")
        return str(unity_exe)

    # Method 4: Search common drives
    for drive in ["C", "D", "E", "S"]:
        # Pattern 1: {Drive}:\Unity\Hub\{Version}\Editor\Unity.exe
        unity_exe = Path(f"{drive}:\\Unity\\Hub\\{unity_version}\\Editor\\Unity.exe")
        if unity_exe.exists():
            print(f"Found Unity {unity_version} on drive {drive}: {unity_exe}")
            return str(unity_exe)

        # Pattern 2: {Drive}:\Program Files\Unity\Hub\Editor\{Version}\Editor\Unity.exe
        unity_exe = Path(f"{drive}:\\Program Files\\Unity\\Hub\\Editor\\{unity_version}\\Editor\\Unity.exe")
        if unity_exe.exists():
            print(f"Found Unity {unity_version} on drive {drive}: {unity_exe}")
            return str(unity_exe)

    return None


def find_unity_exe(project_path: Optional[Path] = None) -> str:
    """
    Find Unity.exe path with automatic version detection.

    Checks in order (highest to lowest priority):
    1. pytest --unity-exe command-line argument (sets UNITY_EXE env var)
    2. UNITY_EXE environment variable (manual override)
    3. Automatic detection based on project's Unity version:
       - Reads version from ProjectSettings/ProjectVersion.txt
       - Searches Unity Hub installation locations for that specific version
       - Checks custom install paths, standard locations, and multiple drives

    Args:
        project_path: Path to Unity project root. If None, tries to auto-detect from environment.

    Returns:
        Path to Unity.exe

    Raises:
        FileNotFoundError: If Unity.exe cannot be found
    """
    # 1. Check UNITY_EXE environment variable (manual override)
    unity_exe = os.environ.get("UNITY_EXE")
    if unity_exe and Path(unity_exe).exists():
        print(f"Using Unity from UNITY_EXE environment variable: {unity_exe}")
        return unity_exe

    # 2. Auto-detect project path if not provided
    if project_path is None:
        # Try to get from NYAMU_WORKER_PROJECT_PATH environment variable (set by conftest.py)
        worker_project = os.environ.get("NYAMU_WORKER_PROJECT_PATH")
        if worker_project:
            project_path = Path(worker_project)
        else:
            # Fall back to default project path
            current_file = Path(__file__)
            project_path = current_file.parent.parent / "Nyamu.UnityTestProject"

    # 3. Read Unity version from project
    try:
        unity_version = get_unity_version_from_project(project_path)
        print(f"Project requires Unity version: {unity_version}")
    except (FileNotFoundError, ValueError) as e:
        raise FileNotFoundError(
            f"Cannot determine Unity version from project at {project_path}: {e}\n"
            "Set UNITY_EXE environment variable to manually specify Unity.exe path.\n"
            "Example: set UNITY_EXE=C:\\Program Files\\Unity\\Hub\\Editor\\2021.3.45f2\\Editor\\Unity.exe"
        )

    # 4. Find Unity.exe for the specific version
    unity_exe_path = find_unity_exe_for_version(unity_version)

    if unity_exe_path:
        return unity_exe_path

    # Unity not found - provide helpful error message
    raise FileNotFoundError(
        f"Unity Editor {unity_version} not found!\n\n"
        "This script searched for Unity in the following locations:\n"
        "  - Unity Hub secondary install path (%APPDATA%\\UnityHub\\secondaryInstallPath.json)\n"
        "  - Standard paths on drives C, D, E, S\n\n"
        f"Please ensure Unity {unity_version} is installed via Unity Hub.\n"
        "Alternatively, set UNITY_EXE environment variable to Unity.exe path:\n"
        f"  Example: set UNITY_EXE=C:\\Program Files\\Unity\\Hub\\Editor\\{unity_version}\\Editor\\Unity.exe"
    )


def pre_register_project_port(unity_exe_path: str, project_path: Path, port: int, timeout: int = 60) -> bool:
    """
    Pre-register project port in global registry using Unity batch-mode.

    This prevents race conditions when multiple Unity instances start simultaneously.
    Uses a global file lock to ensure sequential registration across all workers.

    Args:
        unity_exe_path: Path to Unity.exe
        project_path: Path to Unity project
        port: Port to register for this project
        timeout: Maximum time to wait for registration (seconds)

    Returns:
        True if registration succeeded, False otherwise
    """
    # Use project-local Temp directory to avoid antivirus scanning
    # Find project root (D:\code\Nyamu) by going up from this file
    project_root = Path(__file__).parent.parent
    lock_dir = project_root / "Temp" / "nyamu_unity_locks"
    lock_dir.mkdir(parents=True, exist_ok=True)
    lock_file = lock_dir / "nyamu_registry_lock.lock"

    print(f"  Pre-registering project port {port} (with global lock)...")

    lock = filelock.FileLock(str(lock_file), timeout=30)

    try:
        with lock:
            # Run Unity in batch-mode to trigger NyamuSettings.Reload
            # This will register the port in NyamuProjectsRegistry.json
            log_file = project_path / ".nyamu" / "pre-registration.log"
            log_file.parent.mkdir(parents=True, exist_ok=True)

            cmd = [
                unity_exe_path,
                "-batchmode",
                "-nographics",
                "-quit",
                "-projectPath", str(project_path),
                "-logFile", str(log_file),
            ]

            print(f"    Running: {' '.join(cmd)}")

            start_time = time.time()
            result = subprocess.run(
                cmd,
                cwd=str(project_path),
                timeout=timeout,
                capture_output=True
            )

            elapsed = time.time() - start_time

            if result.returncode == 0:
                print(f"    Pre-registration completed successfully in {elapsed:.1f}s")
                return True
            else:
                print(f"    Pre-registration exited with code {result.returncode} after {elapsed:.1f}s")
                # Not a critical failure - Unity will retry registration on main startup
                return False

    except subprocess.TimeoutExpired:
        print(f"    Pre-registration timed out after {timeout}s")
        return False
    except filelock.Timeout:
        print(f"    Could not acquire registry lock within 30s - another worker is registering")
        return False
    except Exception as e:
        print(f"    Pre-registration failed: {e}")
        return False


class UnityInstanceManager:
    """Manages Unity Editor batch-mode instance lifecycle."""

    def __init__(self, unity_exe_path: str, project_path: Path, port: int):
        """
        Initialize Unity instance manager.

        Args:
            unity_exe_path: Path to Unity.exe
            project_path: Path to Unity project
            port: HTTP server port for MCP server
        """
        self.unity_exe_path = unity_exe_path
        self.project_path = Path(project_path)
        self.port = port
        self.process: Optional[subprocess.Popen] = None
        self.log_file: Optional[Path] = None

    async def start_unity(self, timeout: int = 120):
        """
        Start Unity in batch-mode and wait for it to be ready.

        Args:
            timeout: Maximum time to wait for Unity to start (seconds)

        Raises:
            TimeoutError: If Unity doesn't start within timeout
            FileNotFoundError: If Unity.exe or project path doesn't exist
        """
        # Verify paths exist
        if not Path(self.unity_exe_path).exists():
            raise FileNotFoundError(f"Unity.exe not found: {self.unity_exe_path}")

        if not self.project_path.exists():
            raise FileNotFoundError(f"Unity project not found: {self.project_path}")

        # Set up log file
        log_dir = self.project_path / ".nyamu"
        log_dir.mkdir(parents=True, exist_ok=True)
        self.log_file = log_dir / "unity.log"

        # Launch Unity in batch-mode
        cmd = [
            self.unity_exe_path,
            "-batchmode",
            "-nographics",
            "-projectPath", str(self.project_path),
        ]

        print(f"Starting Unity batch-mode: {' '.join(cmd)}")

        with open(self.log_file, 'w') as log:
            self.process = subprocess.Popen(
                cmd,
                stdout=log,
                stderr=subprocess.STDOUT,
                cwd=str(self.project_path)
            )

        print(f"  Unity process started (PID: {self.process.pid})")
        print(f"  Log file: {self.log_file}")

        # Wait for Unity to be ready
        try:
            await self.wait_for_unity_ready(timeout)
            print(f"  Unity ready on port {self.port}")
        except TimeoutError:
            await self.stop_unity()
            raise

    async def wait_for_unity_ready(self, timeout: int):
        """
        Wait for Unity HTTP server to be ready.

        Args:
            timeout: Maximum time to wait (seconds)

        Raises:
            TimeoutError: If Unity doesn't become ready within timeout
        """
        start_time = time.time()
        health_url = f"http://localhost:{self.port}/scripts-compile-status"

        print(f"  Waiting for Unity HTTP server on port {self.port}...")

        while time.time() - start_time < timeout:
            # Check if process is still running
            if self.process and self.process.poll() is not None:
                raise RuntimeError(
                    f"Unity process terminated unexpectedly. Check log: {self.log_file}"
                )

            # Try to connect to HTTP server
            try:
                response = requests.get(health_url, timeout=2)
                if response.status_code == 200:
                    return  # Unity is ready!
            except requests.RequestException:
                pass  # Not ready yet

            await asyncio.sleep(2)

        elapsed = time.time() - start_time
        raise TimeoutError(
            f"Unity HTTP server not ready after {elapsed:.1f}s. "
            f"Check log: {self.log_file}"
        )

    async def stop_unity(self):
        """Stop Unity instance gracefully."""
        if not self.process:
            return

        print(f"Stopping Unity process (PID: {self.process.pid})...")

        # Try graceful termination first
        try:
            self.process.terminate()
            await asyncio.sleep(10)

            # Force kill if still running
            if self.process.poll() is None:
                print("  Unity didn't stop gracefully, force killing...")
                self.process.kill()
                await asyncio.sleep(2)

            print("  Unity stopped")
        except Exception as e:
            print(f"  Warning: Error stopping Unity: {e}")

        self.process = None
