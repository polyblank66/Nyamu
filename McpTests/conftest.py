"""
Pytest configuration and fixtures for NYAMU MCP Server tests
"""

import pytest
import pytest_asyncio
import asyncio
import os
import sys
from pathlib import Path

# Add current directory to Python path
sys.path.insert(0, str(Path(__file__).parent))

from mcp_client import MCPClient
from unity_helper import UnityHelper, UnityStateManager


@pytest_asyncio.fixture(scope="function")
async def mcp_client():
    """Fixture for MCP client used in all tests"""
    client = MCPClient()
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
async def unity_state_manager(mcp_client, request):
    """Fixture for Unity State Manager with three-tier cleanup selection"""
    manager = UnityStateManager(mcp_client)

    # Determine cleanup level needed for this test
    cleanup_level = _get_cleanup_level(request)

    # Light pre-test check - skip for protocol tests that don't need Unity state
    if cleanup_level != "noop":
        try:
            await manager.refresh_assets(force=False)
        except:
            pass  # Non-critical if this fails

    yield manager

    # Smart post-test cleanup based on test type
    print(f"Test {request.node.name} detected as {cleanup_level} - using {cleanup_level} cleanup")
    await manager.ensure_clean_state(cleanup_level=cleanup_level)


@pytest_asyncio.fixture(scope="function")
async def unity_helper(mcp_client):
    """Fixture for Unity Helper with automatic file restoration"""
    helper = UnityHelper(mcp_client=mcp_client)

    yield helper

    # Restore all modified files after each test
    try:
        helper.restore_all_files()
    except Exception as e:
        print(f"Warning: File restoration encountered issues: {e}")


@pytest_asyncio.fixture(scope="function")
async def temp_files(mcp_client):
    """Fixture for tracking temporary files with robust cleanup"""
    created_files = []

    def register_temp_file(file_path):
        created_files.append(file_path)
        return file_path

    yield register_temp_file

    # Comprehensive temporary file cleanup
    if created_files:
        try:
            unity_helper = UnityHelper(mcp_client=mcp_client)
            await unity_helper.cleanup_temp_files_with_refresh(created_files)

        except Exception as e:
            print(f"Warning: Temporary file cleanup encountered issues: {e}")
            # Try individual file cleanup as fallback
            for file_path in created_files:
                try:
                    if os.path.exists(file_path):
                        os.remove(file_path)
                        # Also remove .meta files
                        meta_path = file_path + ".meta"
                        if os.path.exists(meta_path):
                            os.remove(meta_path)
                except Exception as cleanup_error:
                    print(f"Could not remove {file_path}: {cleanup_error}")


@pytest.fixture(autouse=True)
def check_unity_running():
    """Checks that Unity is running and available"""
    # Check that Unity HTTP server is available
    import requests
    try:
        response = requests.get("http://localhost:17932/compile-status", timeout=5)
        if response.status_code != 200:
            pytest.skip("Unity HTTP server unavailable")
    except requests.exceptions.RequestException:
        pytest.skip("Unity not running or HTTP server unavailable")


def pytest_configure(config):
    """Pytest configuration"""
    config.addinivalue_line("markers", "slow: marks slow tests")
    config.addinivalue_line("markers", "compilation: compilation tests")
    config.addinivalue_line("markers", "mcp: MCP protocol tests")
    config.addinivalue_line("markers", "asmdef: Assembly Definition tests")


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

    # Log test state transitions for debugging randomized test issues
    if hasattr(item, '_request') and call.when == "teardown":
        # Test completed, log for debugging if needed
        if report.failed:
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
