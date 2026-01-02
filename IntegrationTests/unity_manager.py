"""
Unity Instance Manager for Parallel Test Execution

Manages Unity Editor batch-mode lifecycle for pytest-xdist workers.
"""

import asyncio
import os
import subprocess
import time
from pathlib import Path
from typing import Optional
import requests


def find_unity_exe() -> str:
    """
    Find Unity.exe path.

    Checks in order:
    1. UNITY_EXE environment variable (highest priority)
    2. Unity Hub default locations (Windows)
    3. Common Unity installation paths

    Returns:
        Path to Unity.exe

    Raises:
        FileNotFoundError: If Unity.exe cannot be found
    """
    # 1. Check UNITY_EXE environment variable
    unity_exe = os.environ.get("UNITY_EXE")
    if unity_exe and Path(unity_exe).exists():
        return unity_exe

    # 2. Check Unity Hub default locations (Windows)
    hub_paths = [
        Path("C:/Program Files/Unity/Hub/Editor"),
        Path("C:/Program Files/Unity/Editor"),
        Path("D:/Program Files/Unity"),
        Path("C:/Program Files/Unity"),
    ]

    for hub_path in hub_paths:
        if hub_path.exists():
            # Look for Editor/Unity.exe in version subdirectories
            for version_dir in sorted(hub_path.glob("*"), reverse=True):
                unity_exe_path = version_dir / "Editor" / "Unity.exe"
                if unity_exe_path.exists():
                    return str(unity_exe_path)

            # Direct Unity.exe in hub path
            unity_exe_path = hub_path / "Unity.exe"
            if unity_exe_path.exists():
                return str(unity_exe_path)

    raise FileNotFoundError(
        "Unity.exe not found. Set UNITY_EXE environment variable to Unity.exe path. "
        "Example: set UNITY_EXE=C:\\Program Files\\Unity\\Hub\\Editor\\2022.3.50f1\\Editor\\Unity.exe"
    )


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
