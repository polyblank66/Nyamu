"""
Unity Project Manager for Parallel Test Execution

Manages Unity project copies for pytest-xdist workers.
"""

import os
import shutil
import json
from pathlib import Path
from typing import Optional


class ProjectManager:
    """Manages Unity project copies for parallel worker instances."""

    def __init__(self, base_project_path: Path, worker_id: str):
        """
        Initialize project manager.

        Args:
            base_project_path: Path to main Unity project (Nyamu.UnityTestProject)
            worker_id: Worker ID from pytest-xdist (e.g., 'gw0', 'gw1', 'master')
        """
        self.base_project_path = Path(base_project_path)
        self.worker_id = worker_id
        self.worker_project_path = self._compute_worker_path()

    def _compute_worker_path(self) -> Path:
        """Compute worker-specific project path."""
        if self.worker_id == "master":
            return self.base_project_path
        else:
            return self.base_project_path.parent / f"{self.base_project_path.name}.worker_{self.worker_id}"

    def get_worker_project_path(self) -> Path:
        """Get the worker-specific Unity project path."""
        return self.worker_project_path

    def create_or_sync_worker_project(self) -> Path:
        """
        Create or synchronize worker project copy.

        Returns:
            Path to worker project
        """
        # Skip if we're master worker (no copy needed)
        if self.worker_id == "master":
            return self.base_project_path

        # Check if we should skip sync
        skip_sync = os.environ.get("NYAMU_SKIP_SYNC", "false").lower() == "true"

        if self.worker_project_path.exists():
            if skip_sync:
                print(f"Skipping project sync for {self.worker_id} (NYAMU_SKIP_SYNC=true)")
                return self.worker_project_path
            else:
                print(f"Syncing project for {self.worker_id}...")
                self.sync_code_changes()
        else:
            print(f"Creating fresh project copy for {self.worker_id}...")
            self._create_full_copy()

        return self.worker_project_path

    def _create_full_copy(self):
        """Create full Unity project copy (first run)."""
        # Create worker project directory
        self.worker_project_path.mkdir(parents=True, exist_ok=True)

        # Directories to copy
        dirs_to_copy = ["Assets", "Packages", "ProjectSettings"]

        for dir_name in dirs_to_copy:
            src_dir = self.base_project_path / dir_name
            dst_dir = self.worker_project_path / dir_name

            if src_dir.exists():
                print(f"  Copying {dir_name}/...")
                if dst_dir.exists():
                    shutil.rmtree(dst_dir)
                shutil.copytree(src_dir, dst_dir)

        # Copy Library/PackageCache to avoid package re-download
        src_package_cache = self.base_project_path / "Library" / "PackageCache"
        if src_package_cache.exists():
            print(f"  Copying Library/PackageCache/...")
            dst_library = self.worker_project_path / "Library"
            dst_library.mkdir(parents=True, exist_ok=True)
            dst_package_cache = dst_library / "PackageCache"
            if dst_package_cache.exists():
                shutil.rmtree(dst_package_cache)
            shutil.copytree(src_package_cache, dst_package_cache)

        print(f"  Project copy created at {self.worker_project_path}")

    def sync_code_changes(self):
        """Synchronize code changes from base project (incremental update)."""
        # For simplicity, we'll do a full sync of critical directories
        # This could be optimized to only sync changed files

        dirs_to_sync = ["Assets", "Packages"]

        for dir_name in dirs_to_sync:
            src_dir = self.base_project_path / dir_name
            dst_dir = self.worker_project_path / dir_name

            if src_dir.exists():
                print(f"  Syncing {dir_name}/...")
                if dst_dir.exists():
                    shutil.rmtree(dst_dir)
                shutil.copytree(src_dir, dst_dir)

    def create_worker_nyamu_config(self, port: int):
        """
        Create worker-specific .nyamu configuration.

        Args:
            port: HTTP server port for this worker
        """
        nyamu_dir = self.worker_project_path / ".nyamu"
        nyamu_dir.mkdir(parents=True, exist_ok=True)

        # Create NyamuSettings.json
        settings_path = nyamu_dir / "NyamuSettings.json"
        settings = {
            "MonoBehaviour": {
                "serverPort": port,
                "manualPortMode": True,
                "responseCharacterLimit": 25000,
                "enableTruncation": True,
                "minLogLevel": 0
            }
        }

        with open(settings_path, 'w') as f:
            json.dump(settings, f, indent=2)

        # Create nyamu.bat with worker-specific port
        # Reference shared mcp-server.js from Nyamu.UnityPackage/Node/
        mcp_server_js = self.base_project_path / "Packages" / "dev.polyblank.nyamu" / "Node" / "mcp-server.js"
        worker_log = nyamu_dir / "mcp-server.log"

        bat_path = nyamu_dir / "nyamu.bat"
        bat_content = f'''@echo off
node "{mcp_server_js}" --port {port} --log-file "{worker_log}" %*
'''

        with open(bat_path, 'w') as f:
            f.write(bat_content)

        print(f"  Created .nyamu config for {self.worker_id} on port {port}")

    def cleanup_worker_project(self):
        """Remove worker project copy (cleanup after tests)."""
        if self.worker_id == "master":
            return  # Don't clean up main project

        if self.worker_project_path.exists():
            print(f"Cleaning up worker project {self.worker_id}...")
            shutil.rmtree(self.worker_project_path)
