# Integration Tests - Parallel and Serial Execution Plan

## Overview

Support three execution modes for Nyamu integration tests:

1. **Serial Mode (Manual Unity)**: Traditional development workflow - Unity GUI + sequential tests
2. **Serial Mode (Batch Unity)**: Automated single-instance testing - headless Unity + sequential tests
3. **Parallel Mode**: Fast concurrent testing - multiple headless Unity instances + parallel tests

All modes maintain full backward compatibility and use the same test infrastructure.

## Quick Reference

| Mode | Command | Setup | Time | Memory | Best For |
|------|---------|-------|------|--------|----------|
| **Serial (Manual)** | `python -m pytest` | Open Unity manually | ~100s | 4-6GB | Development, debugging |
| **Serial (Batch)** | `NYAMU_SERIAL_BATCH_MODE=true python -m pytest` | Set `UNITY_EXE` | ~125s | 4-6GB | Lightweight CI, Docker |
| **Parallel (2x)** | `python -m pytest -n 2` | Set `UNITY_EXE` | ~90s | 8-12GB | Fast CI/CD |
| **Parallel (4x)** | `python -m pytest -n 4` | Set `UNITY_EXE` | ~80s | 16-24GB | Large test suites |

## User Confirmed Decisions

- ✅ Unity batch-mode DOES support EditorApplication.update and static fields (just headless mode)
- ✅ Use pytest-xdist for parallel test distribution
- ✅ Support 2 parallel instances by default (configurable)
- ✅ Maintain backward compatibility with existing serial mode
- ✅ Support both manual Unity (GUI) and batch-mode Unity for serial execution

## Execution Modes

### Serial Mode (Default)
**When to use**: Development, debugging, single-threaded CI, or when Unity GUI is needed

**Characteristics**:
- One Unity Editor instance (manual GUI or batch-mode)
- Tests run sequentially (no pytest-xdist)
- Faster startup (no project copying)
- Lower memory usage (~4-6GB)
- Easy debugging with Unity GUI

**Two variants**:
1. **Manual Unity** (default): Developer opens Unity Editor manually
2. **Batch-mode Unity**: Unity launched automatically in headless mode

### Parallel Mode
**When to use**: Fast test execution, CI/CD pipelines, regression testing

**Characteristics**:
- Multiple Unity batch-mode instances (default: 2)
- Tests run concurrently via pytest-xdist
- Slower startup (project copy + Unity launch)
- Higher memory usage (~8-12GB for 2 workers)
- Faster overall execution (1.5-2x speedup)

### Mode Comparison

| Aspect | Serial (Manual) | Serial (Batch) | Parallel (2 workers) |
|--------|----------------|----------------|----------------------|
| Execution | Sequential | Sequential | Concurrent |
| Unity Instance | Manual GUI | Auto batch-mode | Auto batch-mode (x2) |
| Startup Time | 0s (already open) | ~20-30s | ~25-35s |
| Test Execution | ~100s | ~100s | ~60-65s |
| Total Time | ~100s | ~120-130s | ~85-100s |
| Memory | ~4-6GB | ~4-6GB | ~8-12GB |
| Use Case | Development | Lightweight CI | Fast CI/CD |
| Debugging | Easy (GUI) | Logs only | Logs only |

## Architecture

### Current State (Serial Mode)
- Tests run serially against single Unity Editor instance
- MCPClient uses stdio: pytest → nyamu.bat → node.js → HTTP → Unity Editor
- Port auto-assigned by NyamuProjectRegistry (17542+)
- 21 test files, 140+ tests, three-tier cleanup (noop/minimal/full)
- Developer manually opens Unity Editor before running tests

### Target State (Dual Mode Support)

**Serial Mode**:
- Single Unity instance (manual or batch-mode)
- No project copying
- Port: 17542 (from NyamuProjectRegistry or settings)
- Project path: `Nyamu.UnityTestProject` (main project)

**Parallel Mode**:
- N pytest-xdist workers (default: 2, configurable)
- Worker-specific project copies: `Nyamu.UnityTestProject.worker_gw0`, `Nyamu.UnityTestProject.worker_gw1`
- Port assignment: Worker 0 → 17542, Worker 1 → 17543, etc.
- One Unity batch-mode instance per worker (session-scoped)
- Project copies synchronized from main before test session

---

## Implementation Components

### 1. New Files to Create

#### `IntegrationTests/project_manager.py`
**Purpose**: Manage Unity project copies for parallel workers

**Key class: ProjectManager**
```python
class ProjectManager:
    def __init__(self, base_project_path: Path, worker_id: str)
    def get_worker_project_path(self) -> Path
    def create_or_sync_worker_project(self) -> Path
    def sync_code_changes(self)
    def create_worker_nyamu_config(self, port: int)
    def cleanup_worker_project()
```

**Copy strategy**:
- **Full copy on first run**: Copy `Assets/`, `Packages/`, `ProjectSettings/` (include .meta files)
- **Skip**: `Library/`, `Temp/`, `Logs/` (recreated by Unity)
- **Generate fresh**: `.nyamu/` folder with worker-specific port
- **Incremental sync**: On subsequent runs, only update changed files in Assets/Packages/

**NyamuSettings.json generation**:
```json
{
  "MonoBehaviour": {
    "serverPort": <worker_port>,
    "manualPortMode": true,
    "responseCharacterLimit": 25000,
    "enableTruncation": true,
    "minLogLevel": 0
  }
}
```

**nyamu.bat generation**:
```batch
@echo off
node "<shared_mcp-server.js_path>" --port <worker_port> --log-file "<worker_log>" %*
```

**Important**: Share single mcp-server.js from `Nyamu.UnityPackage/Node/`, don't copy per worker.

---

#### `IntegrationTests/unity_manager.py`
**Purpose**: Manage Unity Editor batch-mode lifecycle

**Key class: UnityInstanceManager**
```python
class UnityInstanceManager:
    def __init__(self, unity_exe_path: str, project_path: Path, port: int)
    async def start_unity(self, timeout: int = 120)
    async def wait_for_unity_ready(self, timeout: int)
    async def stop_unity()
```

**Unity batch-mode command**:
```bash
Unity.exe -batchmode -nographics -projectPath "D:\code\Nyamu\Nyamu.UnityTestProject.worker_gw0"
```

**Health check**: Poll `http://localhost:{port}/scripts-compile-status` until 200 response

**Logging**: Capture stdout/stderr to `.nyamu/unity.log` for debugging

**Shutdown**: Graceful terminate with 10s timeout, then force kill

---

**Unity.exe detection function**:
```python
def find_unity_exe() -> str:
    # 1. Check UNITY_EXE environment variable (highest priority)
    # 2. Check Unity Hub default locations (Windows):
    #    - C:\Program Files\Unity\Hub\Editor\*\Editor\Unity.exe
    #    - C:\Program Files\Unity\Editor\Unity.exe
    # 3. If not found: pytest.skip("Unity.exe not found. Set UNITY_EXE for parallel mode.")
```

---

### 2. Files to Modify

#### `IntegrationTests/conftest.py`
**Critical changes**:

**Add worker detection in pytest_configure**:
```python
def pytest_configure(config):
    # Existing marker registration...

    # Get worker ID from pytest-xdist
    worker_id = getattr(config, 'workerinput', {}).get('workerid', 'master')
    config.worker_id = worker_id
```

**Add session-scoped fixtures**:
```python
@pytest.fixture(scope="session")
def worker_id(request):
    """Get worker ID (master for single, gw0/gw1/etc for parallel)"""
    return getattr(request.config, 'worker_id', 'master')

@pytest.fixture(scope="session")
def worker_port(worker_id):
    """Get Unity HTTP server port for this worker"""
    if worker_id == "master":
        return 17542  # Base port from NyamuProjectRegistry
    else:
        worker_num = int(worker_id[2:])  # "gw0" -> 0
        return 17542 + worker_num

@pytest.fixture(scope="session")
def worker_project_path(worker_id):
    """Get Unity project path for this worker"""
    base_path = Path(__file__).parent.parent / "Nyamu.UnityTestProject"

    if worker_id == "master":
        return base_path
    else:
        return base_path.parent / f"{base_path.name}.{worker_id}"

@pytest.fixture(scope="session", autouse=True)
async def setup_worker_environment(worker_id, worker_port, worker_project_path):
    """Session-level setup: manages Unity instances for both serial and parallel modes"""

    # Check if serial batch-mode is enabled
    serial_batch_mode = os.environ.get("NYAMU_SERIAL_BATCH_MODE", "false").lower() == "true"

    if worker_id != "master":
        # === PARALLEL MODE ===
        # Create/sync project copy for this worker
        from project_manager import ProjectManager

        base_path = Path(__file__).parent.parent / "Nyamu.UnityTestProject"
        manager = ProjectManager(base_path, worker_id)

        # Create or sync project
        manager.create_or_sync_worker_project()

        # Create .nyamu config with worker-specific port
        manager.create_worker_nyamu_config(worker_port)

        # Start Unity batch-mode instance
        from unity_manager import UnityInstanceManager, find_unity_exe

        unity_exe = find_unity_exe()
        unity_manager = UnityInstanceManager(unity_exe, worker_project_path, worker_port)

        try:
            await unity_manager.start_unity(timeout=120)
        except TimeoutError as e:
            pytest.skip(f"Unity instance failed to start: {e}")

        yield

        # Stop Unity instance
        await unity_manager.stop_unity()

        # Optional cleanup (disabled by default for faster reruns)
        # if os.environ.get("NYAMU_CLEANUP_WORKERS") == "true":
        #     manager.cleanup_worker_project()

    elif serial_batch_mode:
        # === SERIAL MODE (BATCH-MODE UNITY) ===
        # Launch single Unity batch-mode instance for serial execution
        from unity_manager import UnityInstanceManager, find_unity_exe

        unity_exe = find_unity_exe()
        unity_manager = UnityInstanceManager(unity_exe, worker_project_path, worker_port)

        try:
            await unity_manager.start_unity(timeout=120)
        except TimeoutError as e:
            pytest.skip(f"Unity instance failed to start: {e}")

        yield

        # Stop Unity instance
        await unity_manager.stop_unity()

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
```

**Update mcp_client fixture**:
```python
@pytest_asyncio.fixture(scope="function")
async def mcp_client(worker_project_path):
    """Fixture for MCP client - now uses worker-specific project path"""
    mcp_server_path = worker_project_path / ".nyamu" / "nyamu.bat"

    client = MCPClient(mcp_server_path=str(mcp_server_path))
    await client.start()

    yield client

    await client.stop()
```

**Update unity_helper fixture**:
```python
@pytest_asyncio.fixture(scope="function")
async def unity_helper(mcp_client, worker_project_path):
    """Fixture for Unity Helper - now uses worker-specific project path"""
    helper = UnityHelper(project_root=str(worker_project_path.parent), mcp_client=mcp_client)

    yield helper

    try:
        helper.restore_all_files()
    except Exception as e:
        print(f"Warning: File restoration encountered issues: {e}")
```

**Remove check_unity_running autouse fixture**: Replaced by setup_worker_environment

**Location**: `D:\code\Nyamu\IntegrationTests\conftest.py`

---

#### `IntegrationTests/mcp_client.py`
**No changes required!**

Already accepts optional `mcp_server_path` parameter in `__init__`:
```python
def __init__(self, mcp_server_path: str = None):
```

Fixture will pass worker-specific path via dependency injection.

**Location**: `D:\code\Nyamu\IntegrationTests\mcp_client.py`

---

#### `IntegrationTests/unity_helper.py`
**No changes required!**

Already accepts optional `project_root` parameter in both classes:
```python
# UnityHelper
def __init__(self, project_root: str = None, mcp_client = None):
```

Fixture will pass worker-specific project root via dependency injection.

**Location**: `D:\code\Nyamu\IntegrationTests\unity_helper.py`

---

#### `IntegrationTests/requirements.txt`
**Add pytest-xdist**:
```
pytest>=9.0.0
pytest-asyncio>=1.3.0
requests==2.31.0
aiohttp==3.9.1
mcp>=1.0.0
trio>=0.32.0
pytest-xdist>=3.0.0
```

**Location**: `D:\code\Nyamu\IntegrationTests\requirements.txt`

---

#### `.gitignore`
**Add worker project copies**:
```gitignore
# Parallel test worker Unity project copies
Nyamu.UnityTestProject.worker_*/
```

**Location**: `D:\code\Nyamu\.gitignore`

---

### 3. Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `UNITY_EXE` | For batch-mode | - | Path to Unity.exe (e.g., `C:\Program Files\Unity\Hub\Editor\2022.3.50f1\Editor\Unity.exe`). Required for parallel mode and serial batch-mode. |
| `NYAMU_SERIAL_BATCH_MODE` | No | false | Enable batch-mode Unity for serial execution. Set to `true` to auto-launch Unity in headless mode without pytest-xdist. |
| `NYAMU_SKIP_SYNC` | No | false | Skip project sync in parallel mode (faster reruns when code unchanged). Only applies to parallel mode. |
| `NYAMU_CLEANUP_WORKERS` | No | false | Clean up worker project copies after test session. Set to `true` in CI/CD. Only applies to parallel mode. |
| `NYAMU_MAX_INSTANCES` | No | 2 | Maximum parallel instances (informational, actual value set via pytest `-n` flag). |

---

### 4. Usage

#### Mode 1: Serial Mode - Manual Unity (Default)
**Use case**: Development, debugging, when you need Unity GUI

```bash
# 1. Manually open Unity Editor with Nyamu.UnityTestProject
# 2. Run tests
cd IntegrationTests
python -m pytest
```

**Characteristics**:
- ✅ Unity GUI available for debugging
- ✅ No startup overhead (Unity already open)
- ✅ Full Unity Editor features
- ❌ Requires manual Unity launch
- ❌ Tests run serially (~100s)

---

#### Mode 2: Serial Mode - Batch-Mode Unity
**Use case**: Lightweight CI, Docker containers, when Unity GUI not needed

```bash
# 1. Set Unity.exe path
set UNITY_EXE=C:\Program Files\Unity\Hub\Editor\2022.3.50f1\Editor\Unity.exe

# 2. Enable serial batch-mode
set NYAMU_SERIAL_BATCH_MODE=true

# 3. Run tests (Unity launches automatically in headless mode)
cd IntegrationTests
python -m pytest
```

**Characteristics**:
- ✅ Fully automated (no manual Unity launch)
- ✅ Headless mode (works in Docker, CI)
- ✅ Lower memory than parallel mode
- ❌ ~20-30s startup overhead (Unity launch)
- ❌ Tests run serially (~100s execution)

---

#### Mode 3: Parallel Mode - Multiple Batch-Mode Unity Instances
**Use case**: Fast CI/CD, regression testing, when speed matters

```bash
# 1. Set Unity.exe path
set UNITY_EXE=C:\Program Files\Unity\Hub\Editor\2022.3.50f1\Editor\Unity.exe

# 2. Run with 2 workers (Unity launched automatically in batch-mode)
cd IntegrationTests
python -m pytest -n 2
```

**Characteristics**:
- ✅ Fastest execution (~60-65s for tests)
- ✅ Fully automated
- ✅ Scales with more workers
- ❌ Higher memory usage (~8-12GB for 2 workers)
- ❌ ~25-35s startup overhead (project copy + Unity launch)

---

#### Parallel Mode Optimizations

**Fast Rerun (Skip Project Sync)**:
```bash
set NYAMU_SKIP_SYNC=true
python -m pytest -n 2
```
Use when code hasn't changed between runs. Saves 5-10s on startup.

**CI/CD Mode (Clean Workers After)**:
```bash
set NYAMU_CLEANUP_WORKERS=true
python -m pytest -n 2
```
Ensures clean state for next run. Recommended for CI/CD pipelines.

**Custom Worker Count**:
```bash
# Run with 4 workers
python -m pytest -n 4

# Run with auto-detection (number of CPUs)
python -m pytest -n auto
```

---

### 5. Error Handling

| Error Scenario | Detection | Handling |
|----------------|-----------|----------|
| Unity.exe not found | On setup_worker_environment | pytest.skip("Unity.exe not found. Set UNITY_EXE for parallel mode.") |
| Unity startup timeout | wait_for_unity_ready exceeds timeout | pytest.skip(f"Unity instance failed to start: {e}"), log to .nyamu/unity.log |
| Unity crashes mid-test | Connection errors in MCP client | Test fails, session terminates, log location provided |
| Port already in use | Before Unity startup | pytest.skip(f"Port {port} already in use for worker {worker_id}") |
| Project sync fails | During create_or_sync_worker_project | pytest.skip(f"Cannot sync project: {e}") |

---

### 6. Testing Strategy

**Validation tests for all modes**:

**Serial Mode - Manual Unity**:
1. ✅ Default mode: Open Unity manually, run `python -m pytest -v`
2. ✅ Unity not running: Run without Unity open, expect skip with helpful message

**Serial Mode - Batch Unity**:
3. ✅ Batch-mode launch: `set UNITY_EXE=... && set NYAMU_SERIAL_BATCH_MODE=true && python -m pytest -v`
4. ✅ Unity.exe not found: Unset `UNITY_EXE`, set batch-mode, expect skip

**Parallel Mode**:
5. ✅ Parallel with 1 worker: `set UNITY_EXE=... && python -m pytest -n 1 -v`
6. ✅ Parallel with 2 workers: `python -m pytest -n 2 -v`
7. ✅ Parallel with 4 workers: `python -m pytest -n 4 -v`
8. ✅ Unity.exe not found: Unset `UNITY_EXE`, run `-n 2`, expect skip
9. ✅ Port conflict: Start process on 17542, run tests, expect skip or error
10. ✅ Project sync with skip: `set NYAMU_SKIP_SYNC=true`, run `-n 2`

**Expected performance**:
- **Serial (manual)**: 0s startup + 100s tests = **~100s total**
- **Serial (batch)**: 25s startup + 100s tests = **~125s total**
- **Parallel (2 workers)**: 30s startup + 60s tests = **~90s total** (1.1x speedup vs serial manual)
- **Parallel (4 workers)**: 35s startup + 45s tests = **~80s total** (1.25x speedup vs serial manual)

**Performance notes**:
- Parallel mode speedup includes startup overhead
- Best for test suites >100s where parallelization benefits outweigh startup costs
- Serial batch-mode useful for CI when Unity GUI not needed but parallel overhead too high

---

### 7. Implementation Order

#### Phase 1: Infrastructure
**Goal**: Foundation without breaking existing tests

1. Create `IntegrationTests/project_manager.py` with ProjectManager class
2. Create `IntegrationTests/unity_manager.py` with UnityInstanceManager class
3. Add `find_unity_exe()` function
4. Update `.gitignore` to exclude `Nyamu.UnityTestProject.worker_*/`
5. Add `pytest-xdist>=3.0.0` to `requirements.txt`

**Validation**: All tests pass in single-instance mode

---

#### Phase 2: Fixture Integration
**Goal**: Add worker-scoped fixtures while maintaining compatibility

1. Add `pytest_configure()` hook for worker ID detection in `conftest.py`
2. Add session-scoped fixtures: `worker_id`, `worker_port`, `worker_project_path`
3. Add `setup_worker_environment` session fixture (autouse)
4. Update `mcp_client` fixture to use `worker_project_path`
5. Update `unity_helper` fixture to use `worker_project_path`
6. Remove `check_unity_running` autouse fixture (replaced by setup_worker_environment)

**Validation**: All tests still pass in single-instance mode

---

#### Phase 3: Parallel Support
**Goal**: Enable multiple workers

1. Implement project copy creation in ProjectManager
2. Implement project synchronization in ProjectManager
3. Implement `.nyamu` config generation per worker
4. Implement Unity batch-mode launch in UnityInstanceManager
5. Test with 2 workers

**Validation**: `python -m pytest -n 2` works and shows speedup

---

#### Phase 4: Polish
**Goal**: Production-ready

1. Add comprehensive error messages
2. Optimize project sync (skip unchanged files)
3. Add Unity.exe auto-detection fallbacks
4. Update `IntegrationTests/CLAUDE.md` with parallel mode usage
5. Performance testing

**Validation**: Documentation clear, all edge cases handled

---

### 8. Critical Files Summary

| File | Status | Changes |
|------|--------|---------|
| `IntegrationTests/project_manager.py` | **NEW** | ProjectManager class, project copy/sync logic |
| `IntegrationTests/unity_manager.py` | **NEW** | UnityInstanceManager class, Unity batch-mode lifecycle |
| `IntegrationTests/conftest.py` | **MODIFY** | Add worker fixtures, session setup/teardown |
| `IntegrationTests/mcp_client.py` | **NO CHANGE** | Already supports custom path via parameter |
| `IntegrationTests/unity_helper.py` | **NO CHANGE** | Already supports custom project_root via parameter |
| `IntegrationTests/requirements.txt` | **MODIFY** | Add pytest-xdist>=3.0.0 |
| `.gitignore` | **MODIFY** | Add Nyamu.UnityTestProject.worker_*/ |

---

### 9. Key Design Decisions

**✅ Share mcp-server.js**: Don't copy per worker, reference from `Nyamu.UnityPackage/Node/`
- Reduces disk usage
- Ensures version consistency
- Simplifies updates

**✅ Port calculation**: Deterministic from worker ID (no coordination needed)
- Serial mode (master): 17542 (from NyamuProjectRegistry)
- Worker 0 (gw0): 17542
- Worker 1 (gw1): 17543
- Worker N (gwN): 17542 + N
- No central registry or locks needed

**✅ Project sync strategy**: Full copy first run, incremental thereafter
- Faster subsequent runs
- `NYAMU_SKIP_SYNC=true` for even faster reruns

**✅ Unity batch-mode**: User confirmed EditorApplication.update works in batch-mode
- No MCP server changes needed
- Standard Unity batch-mode launch

**✅ Backward compatibility**: Single-instance mode unchanged
- No `-n` flag = master worker = current behavior
- All existing tests work without modification

---

### 10. Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Unity batch-mode slower than expected | Overhead negates speedup | Benchmark in Phase 3, adjust timeout values |
| Project sync too slow | Session startup delays | Implement smart sync (skip unchanged), NYAMU_SKIP_SYNC option |
| Port conflicts | Unity fails to start | Pre-flight port check, clear error messages |
| Memory constraints (2 Unity instances) | Out of memory on some machines | Document RAM requirements (16GB recommended), make configurable |

---

## Mode Selection Guide

Choose the execution mode based on your use case:

| Scenario | Recommended Mode | Command |
|----------|------------------|---------|
| **Development & debugging** | Serial (manual) | `python -m pytest` (Unity open manually) |
| **Quick local testing** | Serial (manual) | `python -m pytest` (Unity open manually) |
| **Lightweight CI** | Serial (batch) | `UNITY_EXE=... NYAMU_SERIAL_BATCH_MODE=true python -m pytest` |
| **Docker/containerized CI** | Serial (batch) | `UNITY_EXE=... NYAMU_SERIAL_BATCH_MODE=true python -m pytest` |
| **Fast CI/CD pipeline** | Parallel (2-4 workers) | `UNITY_EXE=... python -m pytest -n 2` |
| **Regression testing** | Parallel (2-4 workers) | `UNITY_EXE=... python -m pytest -n 2` |
| **Large test suite (>200s)** | Parallel (4+ workers) | `UNITY_EXE=... python -m pytest -n 4` |

**Decision factors**:
- **Time**: Parallel fastest for execution, serial manual fastest for startup
- **Memory**: Serial uses ~4-6GB, parallel uses ~8-12GB (2 workers)
- **Debugging**: Serial manual provides Unity GUI, others are headless
- **Automation**: Batch modes fully automated, manual requires Unity open

---

## Summary

This implementation supports three execution modes for integration tests:

1. **Serial Mode (Manual Unity)**: Traditional development workflow with Unity GUI
2. **Serial Mode (Batch Unity)**: Automated single-instance testing without GUI
3. **Parallel Mode**: Fast concurrent testing with multiple Unity instances

**Key features**:
- ✅ Full backward compatibility (existing tests work unchanged)
- ✅ Flexible mode selection via environment variables
- ✅ Deterministic port assignment (no coordination needed)
- ✅ Minimal code changes (existing MCPClient and UnityHelper support all modes)
- ✅ Leverages Unity batch-mode support for EditorApplication.update

**Performance**:
- Serial (manual): ~100s (best for development)
- Serial (batch): ~125s (best for lightweight CI)
- Parallel (2 workers): ~90s (best for fast CI/CD)
- Parallel (4 workers): ~80s (best for large test suites)

**Memory requirements**:
- Serial: 4-6GB
- Parallel (2 workers): 8-12GB
- Parallel (4 workers): 16-24GB
