# MCP Tests - Claude Code Instructions

This directory contains Python integration tests for the NYAMU MCP (Model Context Protocol) server. These tests verify MCP functionality including compilation, test execution, and response handling.

## Running Tests

### Serial Execution (Manual Unity)
```bash
# Run all tests (Unity must be open)
cd IntegrationTests && python -m pytest

# Run specific test categories
cd IntegrationTests && python -m pytest -m protocol    # Fast protocol tests
cd IntegrationTests && python -m pytest -m essential   # Core functionality tests
```

### Parallel Execution (Automatic Unity Instances)
```bash
# Set environment variable to use batch-mode Unity
$env:NYAMU_SERIAL_BATCH_MODE="true"

# Run with automatic Unity instance
cd IntegrationTests && python -m pytest -m essential

# Run tests in parallel with automatic Unity instances
cd IntegrationTests && python -m pytest -n 2 -m essential
cd IntegrationTests && python -m pytest -n auto
```

## Test Execution Modes

### Mode 1: Serial with Manual Unity (Default)
- **Setup**: Open Unity Editor manually before running tests
- **Usage**: `pytest` (no environment variables)
- **Best for**: Development with live Unity Editor
- **Unity instances**: 1 (manual)

### Mode 2: Serial with Batch-Mode Unity
- **Setup**: Set `NYAMU_SERIAL_BATCH_MODE=true`
- **Usage**: `pytest -m essential`
- **Best for**: CI/CD or headless testing
- **Unity instances**: 1 (auto-started)

### Mode 3: Parallel with Batch-Mode Unity
- **Setup**: No environment variables needed (pytest-xdist auto-detects)
- **Usage**: `pytest -n 2` or `pytest -n auto`
- **Best for**: Fast test execution
- **Unity instances**: One per worker (auto-started)
- **Performance**: 2-4x faster than serial

## Parallel Testing Infrastructure

### Automatic Unity.exe Detection
- Reads Unity version from `ProjectSettings/ProjectVersion.txt`
- Searches Unity Hub installation locations automatically
- Checks: `secondaryInstallPath.json`, standard paths, multiple drives
- No manual configuration needed (unless using `UNITY_EXE` override)

### Project Isolation
- Each worker gets isolated project copy: `Nyamu.UnityTestProject.worker_gw0/`, `worker_gw1/`, etc.
- Worker-specific ports: 17542 (master), 17543 (gw0), 17544 (gw1), etc.
- Automatic project sync from base project

### Registry Conflict Prevention
- Pre-registration using Unity batch-mode with global file lock
- Prevents race conditions in `NyamuProjectsRegistry.json`
- Only triggers when `.nyamu` config changes (skip on subsequent runs)

### Environment Variables

**Parallel Testing Control:**
- `NYAMU_SERIAL_BATCH_MODE="true"` - Use batch-mode Unity for serial tests
- `NYAMU_SKIP_SYNC="true"` - Skip project sync (faster reruns)
- `NYAMU_CLEANUP_WORKERS="true"` - Remove worker projects after tests

**Manual Unity.exe Override:**
- `UNITY_EXE="C:\Path\To\Unity.exe"` - Environment variable override
- `pytest --unity-exe="C:\Path\To\Unity.exe"` - Command-line argument (highest priority)

**Project Path (Auto-set):**
- `NYAMU_WORKER_PROJECT_PATH` - Set automatically by conftest.py

## Test Prerequisites

1. **Unity Hub** with Unity 2021.3.45f2 (or project version) installed
2. **Python 3.7+** with pytest (`pip install -r requirements.txt`)
3. **Node.js** installed
4. **For manual mode**: Unity Editor running with NYAMU project open
5. **For batch-mode**: Unity installed and auto-detectable

## Key Test Categories

- **Protocol tests** (`@pytest.mark.protocol`): MCP communication, ultra-fast (~0.4s each)
- **Structural tests** (`@pytest.mark.structural`): File modifications, full cleanup
- **Compilation tests**: Unity compilation and error handling
- **Essential tests** (`@pytest.mark.essential`): Core functionality suite (~20s total)

## Performance

The test suite uses a three-tier cleanup system:
- **Protocol tests**: ~0.4s per test (skip Unity operations)
- **Minimal tests**: Fast cleanup for compilation-only tests
- **Structural tests**: Full cleanup for file modifications

**Serial vs Parallel:**
- Essential tests: ~20s (serial) → ~8s (parallel with -n 2)
- Full suite: ~20-25 min (serial) → ~5-10 min (parallel with -n auto)

## Troubleshooting

### Unity not responding
- **Manual mode**: Ensure Unity Editor is open with NYAMU project
- **Batch mode**: Check Unity.exe was found (see startup log)
- **Parallel mode**: Check worker project copies were created

### MCP errors
- Check Node.js installation
- Verify Unity HTTP server is accessible
- **Batch mode**: Check `.nyamu/unity.log` for startup errors

### Test timeouts
- Increase timeout values for slow Unity compilation
- **Parallel mode**: First run is slower (project copying)
- Set `NYAMU_SKIP_SYNC=true` for faster reruns

### Unity.exe not found
- Install Unity via Unity Hub
- Check Unity version matches project (see `ProjectSettings/ProjectVersion.txt`)
- Override with `UNITY_EXE` environment variable if needed

### Worker project issues
- Clean up manually: delete `Nyamu.UnityTestProject.worker_*` directories
- Set `NYAMU_CLEANUP_WORKERS=true` for automatic cleanup
- Set `NYAMU_SKIP_SYNC=false` to force fresh project sync

## Quick Start Examples

### Development (Manual Unity)
```bash
# Open Unity Editor, then:
cd IntegrationTests
pytest -m essential
```

### CI/CD (Automatic Unity)
```bash
# Set batch mode and run in parallel
$env:NYAMU_SERIAL_BATCH_MODE="true"
cd IntegrationTests
pytest -n 2 -m essential
```

### Fast Iteration
```bash
# First run (slow - creates worker projects)
pytest -n 2 -m essential

# Subsequent runs (fast - skips sync)
$env:NYAMU_SKIP_SYNC="true"
pytest -n 2 -m essential
```

### Custom Unity.exe Path
```bash
# Using command-line argument (recommended)
pytest --unity-exe="D:\Unity\2021.3.45f2\Editor\Unity.exe" -m essential

# Using environment variable
$env:UNITY_EXE="D:\Unity\2021.3.45f2\Editor\Unity.exe"
pytest -m essential

# Command-line overrides environment variable
$env:UNITY_EXE="D:\Wrong\Path\Unity.exe"
pytest --unity-exe="D:\Correct\Path\Unity.exe" -m essential
```

See `README.md` for comprehensive documentation and advanced usage.
