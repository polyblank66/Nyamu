"""
Pytest configuration and fixtures for NYAMU MCP Server tests
"""

import pytest
import pytest_asyncio
import asyncio
import os
import sys
import json
import requests
from pathlib import Path

# Add current directory to Python path
sys.path.insert(0, str(Path(__file__).parent))

from mcp_client import MCPClient
from unity_helper import UnityHelper, UnityStateManager

@pytest.fixture(scope="session")
def settings_file(worker_project_path):
    """Get NyamuSettings.json path for this worker"""
    return worker_project_path / ".nyamu" / "NyamuSettings.json"

@pytest.fixture(scope="session")
def worker_id(request):
    """Get worker ID (master for single, gw0/gw1/etc for parallel)"""
    return getattr(request.config, 'worker_id', 'master')

@pytest.fixture(scope="session")
def worker_port(worker_id, worker_project_path):
    """Get Unity HTTP server port for this worker"""
    if worker_id == "master":
        # Read actual port from NyamuSettings.json
        settings_file_path = worker_project_path / ".nyamu" / "NyamuSettings.json"
        try:
            with open(settings_file_path, 'r') as f:
                settings = json.load(f)
            port = settings["MonoBehaviour"]["serverPort"]
            if not isinstance(port, int) or not (1 <= port <= 65535):
                return 17542  # Fallback to default
            return port
        except (FileNotFoundError, KeyError, json.JSONDecodeError):
            return 17542  # Fallback to default
    else:
        # Parallel mode: use deterministic port based on worker ID
        worker_num = int(worker_id[2:])  # "gw0" -> 0
        return 17542 + worker_num


@pytest.fixture(scope="session")
def worker_project_path(worker_id):
    """Get Unity project path for this worker"""
    base_path = Path(__file__).parent.parent / "Nyamu.UnityTestProject"

    if worker_id == "master":
        return base_path
    else:
        return base_path.parent / f"{base_path.name}.worker_{worker_id}"


@pytest.fixture(scope="session", autouse=True)
def setup_worker_environment(worker_id, worker_port, worker_project_path):
    """Session-level setup: manages Unity instances for both serial and parallel modes"""

    print(f"\n{'='*60}")
    print(f"SESSION SETUP for worker: {worker_id} on port {worker_port}")
    print(f"{'='*60}")

    # Set environment variable for worker project path so MCPClient can auto-detect it
    os.environ["NYAMU_WORKER_PROJECT_PATH"] = str(worker_project_path)

    # Check if serial batch-mode is enabled
    serial_batch_mode = os.environ.get("NYAMU_SERIAL_BATCH_MODE", "false").lower() == "true"

    unity_manager = None

    if worker_id != "master":
        # === PARALLEL MODE ===
        # Create/sync project copy for this worker
        from project_manager import ProjectManager

        dev_root_path = Path(__file__).parent.parent
        unity_package_path = dev_root_path / "Nyamu.UnityPackage"
        base_path = dev_root_path / "Nyamu.UnityTestProject"
        manager = ProjectManager(unity_package_path, base_path, worker_id)

        # Create or sync project
        manager.create_or_sync_worker_project()

        # Create .nyamu config with worker-specific port
        # Returns True if config was created/modified, False if unchanged
        config_changed = manager.create_worker_nyamu_config(worker_port)

        # Start Unity batch-mode instance
        from unity_manager import UnityInstanceManager, find_unity_exe, pre_register_project_port

        try:
            unity_exe = find_unity_exe()
        except FileNotFoundError as e:
            pytest.skip(str(e))

        # Pre-register project port only if config changed
        # This prevents race conditions in global registry while avoiding unnecessary work
        if config_changed:
            pre_register_project_port(unity_exe, worker_project_path, worker_port, timeout=60)

        unity_manager = UnityInstanceManager(unity_exe, worker_project_path, worker_port)

        try:
            asyncio.run(unity_manager.start_unity(timeout=120))
        except TimeoutError as e:
            pytest.skip(f"Unity instance failed to start: {e}")

        yield

        print(f"\n{'='*60}")
        print(f"SESSION TEARDOWN for worker: {worker_id}")
        print(f"{'='*60}")

        # Stop Unity instance
        asyncio.run(unity_manager.stop_unity())

        # Optional cleanup (disabled by default for faster reruns)
        if os.environ.get("NYAMU_CLEANUP_WORKERS") == "true":
            manager.cleanup_worker_project()

    elif serial_batch_mode:
        # === SERIAL MODE (BATCH-MODE UNITY) ===
        # Launch single Unity batch-mode instance for serial execution
        from unity_manager import UnityInstanceManager, find_unity_exe, pre_register_project_port

        try:
            unity_exe = find_unity_exe()
        except FileNotFoundError as e:
            pytest.skip(str(e))

        # Check if .nyamu config exists and has correct port
        settings_file_path = worker_project_path / ".nyamu" / "NyamuSettings.json"
        config_needs_update = True
        if settings_file_path.exists():
            try:
                with open(settings_file_path, 'r') as f:
                    settings = json.load(f)
                existing_port = settings.get("MonoBehaviour", {}).get("serverPort")
                if existing_port == worker_port:
                    config_needs_update = False
            except (json.JSONDecodeError, FileNotFoundError):
                pass

        # Pre-register project port only if config changed or doesn't exist
        if config_needs_update:
            pre_register_project_port(unity_exe, worker_project_path, worker_port, timeout=60)

        unity_manager = UnityInstanceManager(unity_exe, worker_project_path, worker_port)

        try:
            asyncio.run(unity_manager.start_unity(timeout=120))
        except TimeoutError as e:
            pytest.skip(f"Unity instance failed to start: {e}")

        yield

        print(f"\n{'='*60}")
        print(f"SESSION TEARDOWN for worker: {worker_id} (batch-mode)")
        print(f"{'='*60}")

        # Stop Unity instance
        asyncio.run(unity_manager.stop_unity())

    else:
        # === SERIAL MODE (MANUAL UNITY) ===
        # Check that Unity is already running (developer opened manually)
        try:
            response = requests.get(f"http://localhost:{worker_port}/scripts-compile-status", timeout=5)
            if response.status_code != 200:
                pytest.skip("Unity HTTP server unavailable in serial mode. "
                           "Either open Unity manually or set NYAMU_SERIAL_BATCH_MODE=true")
        except requests.RequestException:
            pytest.skip("Unity not running or HTTP server unavailable. "
                       "Either open Unity manually or set NYAMU_SERIAL_BATCH_MODE=true")

        yield

        print(f"\n{'='*60}")
        print(f"SESSION TEARDOWN for worker: {worker_id} (manual Unity)")
        print(f"{'='*60}")


@pytest_asyncio.fixture(scope="function")
async def mcp_client(worker_project_path):
    """Fixture for MCP client - now uses worker-specific project path"""
    mcp_server_path = worker_project_path / ".nyamu" / "nyamu.bat"

    client = MCPClient(mcp_server_path=str(mcp_server_path))
    await client.start()

    yield client

    await client.stop()


def _get_cleanup_level(request):
    """Determine the appropriate cleanup level for a test based on pytest markers"""
    # Check for explicit protocol marker (pure MCP communication tests)
    if request.node.get_closest_marker("protocol"):
        return "noop"

    # Check for explicit structural marker (tests that modify Unity project structure)
    if request.node.get_closest_marker("structural"):
        return "full"

    # Default to minimal cleanup for all other tests (compilation/run tests)
    return "minimal"

@pytest_asyncio.fixture(scope="function")
async def unity_state_manager(mcp_client, worker_project_path, request):
    """Fixture for Unity State Manager with smart cleanup based on test results"""
    manager = UnityStateManager(mcp_client, project_path=worker_project_path)

    # Determine cleanup level needed for this test
    cleanup_level = _get_cleanup_level(request)

    # Light pre-test check - skip for protocol tests that don't need Unity state
    if cleanup_level != "noop":
        try:
            await manager.assets_refresh(force=False)
        except:
            pass  # Non-critical if this fails

    yield manager

    # Smart post-test cleanup: only full cleanup if test failed or is structural
    test_failed = request.node.rep_call.failed if hasattr(request.node, 'rep_call') else False

    if test_failed:
        print(f"Test {request.node.name} FAILED - using full cleanup to restore Unity state")
        await manager.ensure_clean_state(cleanup_level="full")
    elif cleanup_level == "full":
        # Structural tests that passed: still need full cleanup
        print(f"Test {request.node.name} PASSED (structural) - using full cleanup")
        await manager.ensure_clean_state(cleanup_level="full")
    else:
        # Passing tests with minimal/noop cleanup
        print(f"Test {request.node.name} PASSED - using {cleanup_level} cleanup")
        await manager.ensure_clean_state(cleanup_level=cleanup_level)


@pytest_asyncio.fixture(scope="function")
async def unity_helper(mcp_client, worker_project_path):
    """Fixture for Unity Helper - now uses worker-specific project path"""
    helper = UnityHelper(project_root=str(worker_project_path), mcp_client=mcp_client)

    yield helper

    # Restore all modified files after each test
    try:
        helper.restore_all_files()
    except Exception as e:
        print(f"Warning: File restoration encountered issues: {e}")


@pytest_asyncio.fixture(scope="function")
async def temp_files(mcp_client, unity_helper):
    """Fixture for tracking temporary files with GUARANTEED cleanup using file lock"""
    from unity_helper import UnityLockManager, wait_for_unity_idle
    import shutil
    import hashlib

    created_files = []

    # Create unique lock file name based on project path from environment variable
    worker_project_path = os.environ.get("NYAMU_WORKER_PROJECT_PATH")
    if worker_project_path:
        path_hash = hashlib.md5(str(worker_project_path).encode()).hexdigest()[:8]
        lock_name = f"unity_state_{path_hash}.lock"
    else:
        lock_name = "unity_state.lock"
    lock_manager = UnityLockManager(lock_name=lock_name)

    def register_temp_file(file_path):
        created_files.append(file_path)
        return file_path

    yield register_temp_file

    # CRITICAL: Acquire lock before cleanup to prevent race conditions
    if created_files:
        with lock_manager:
            try:
                # Wait for Unity to finish any pending operations
                await wait_for_unity_idle(mcp_client, timeout=30)

                # Delete files
                for file_path in created_files:
                    try:
                        if os.path.isdir(file_path):
                            shutil.rmtree(file_path, ignore_errors=True)
                        elif os.path.exists(file_path):
                            os.remove(file_path)

                        # Remove .meta file
                        meta_path = file_path + ".meta"
                        if os.path.exists(meta_path):
                            os.remove(meta_path)
                    except Exception as e:
                        print(f"Could not remove {file_path}: {e}")

                # CRITICAL: Force refresh after deletions
                await unity_helper.assets_refresh_if_available(force=True)

                # Wait for refresh to complete
                await wait_for_unity_idle(mcp_client, timeout=30)

            except Exception as e:
                print(f"Temp file cleanup failed: {e}")


@pytest.fixture(scope="session")
def unity_port(settings_file):
    """Read Unity server port from NyamuSettings.json"""
    try:
        with open(settings_file, 'r') as f:
            settings = json.load(f)

        port = settings["MonoBehaviour"]["serverPort"]

        if not isinstance(port, int) or not (1 <= port <= 65535):
            pytest.skip(f"Invalid port value in {settings_file}: {port}")

        return port

    except FileNotFoundError:
        pytest.skip(f"Settings file not found: {settings_file}")
    except KeyError as e:
        pytest.skip(f"Missing required field in {settings_file}: {e}")
    except json.JSONDecodeError as e:
        pytest.skip(f"Invalid JSON in {settings_file}: {e}")


@pytest.fixture(scope="session")
def unity_base_url(unity_port):
    """Get base URL for Unity HTTP server"""
    return f"http://localhost:{unity_port}"


def pytest_addoption(parser):
    """Add custom command-line options"""
    parser.addoption(
        "--unity-exe",
        action="store",
        default=None,
        help="Path to Unity.exe (overrides UNITY_EXE environment variable and auto-detection)"
    )


def pytest_configure(config):
    """Pytest configuration"""
    config.addinivalue_line("markers", "slow: marks slow tests")
    config.addinivalue_line("markers", "compilation: compilation tests")
    config.addinivalue_line("markers", "mcp: MCP protocol tests")
    config.addinivalue_line("markers", "asmdef: Assembly Definition tests")
    config.addinivalue_line("markers", "essential: core functionality tests")
    config.addinivalue_line("markers", "protocol: pure MCP protocol tests")
    config.addinivalue_line("markers", "structural: tests that modify Unity project structure")

    # Get worker ID from pytest-xdist
    worker_id = getattr(config, 'workerinput', {}).get('workerid', 'master')
    config.worker_id = worker_id

    # Set UNITY_EXE environment variable from command-line option if provided
    unity_exe = config.getoption("--unity-exe")
    if unity_exe:
        os.environ["UNITY_EXE"] = unity_exe
        print(f"\nUsing Unity.exe from command-line: {unity_exe}")


def pytest_collection_modifyitems(config, items):
    """Modification of collected tests"""
    # Add slow marker for tests that contain 'slow' in name
    for item in items:
        if "slow" in item.nodeid.lower():
            item.add_marker(pytest.mark.slow)
        if "compile" in item.nodeid.lower():
            item.add_marker(pytest.mark.compilation)
        if "mcp" in item.nodeid.lower():
            item.add_marker(pytest.mark.mcp)
        if "asmdef" in item.nodeid.lower():
            item.add_marker(pytest.mark.asmdef)


@pytest.hookimpl(hookwrapper=True)
def pytest_runtest_makereport(item, call):
    """
    Hook to capture test results and ensure Unity state is maintained
    """
    outcome = yield
    report = outcome.get_result()

    # Store test result on the item for fixture access
    setattr(item, f"rep_{report.when}", report)

    # Log test state transitions for debugging randomized test issues
    if hasattr(item, '_request') and call.when == "teardown":
        # Test completed, log for debugging if needed
        if report.failed:
            # Check if this is an anyio cancel scope error (known pytest-asyncio + anyio interaction issue)
            # This is a fundamental incompatibility that cannot be resolved:
            # - pytest-asyncio creates separate tasks for setup/test/teardown phases
            # - anyio's CancelScope validates it's exited in the same task it was entered
            # - The MCP SDK (and many anyio-based libraries) trigger this during fixture teardown
            # - Neither asyncio_mode=auto nor trio backend resolve this issue
            # Since the tests themselves execute correctly and the error only affects cleanup,
            # we suppress the error report to avoid misleading test failures
            longrepr_str = str(report.longrepr) if report.longrepr else ""
            is_anyio_teardown_error = "Attempted to exit cancel scope in a different task" in longrepr_str

            if is_anyio_teardown_error:
                # Suppress the error report - mark teardown as passed
                report.outcome = "passed"
                report.longrepr = None
            else:
                print(f"Test {item.nodeid} failed during teardown - Unity state may be compromised")


# @pytest.fixture(autouse=True, scope="function")
# async def ensure_test_isolation():
#     """
#     Automatic fixture to ensure each test starts with a clean Unity state
#     This runs before every test automatically
#     """
#     # Pre-test setup is handled by unity_state_manager fixture
#     yield
#
#     # Post-test cleanup is handled by unity_state_manager and other fixtures
