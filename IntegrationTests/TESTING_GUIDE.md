# Integration Tests Quick Reference

## Fast Test Execution Patterns

### Development Workflow

```bash
# Quick smoke test (fastest, ~5-10s)
pytest -m protocol -n 2

# Essential tests only (~20s serial, ~8s parallel)
pytest -m essential
pytest -m essential -n 2  # Parallel

# Skip slow tests (good for quick iteration)
pytest -m "not slow" -n auto

# Specific category
pytest -m compilation  # Compilation tests only
pytest -m mcp         # MCP protocol tests only
```

### Parallel Execution

```bash
# Use all CPU cores (2-4x faster)
pytest -n auto

# Specific number of workers
pytest -n 2  # Good for dual-core
pytest -n 4  # Good for quad-core

# Parallel with filtering
pytest -m essential -n 2
pytest -m "not slow" -n auto
```

### Test Selection by Pattern

```bash
# Run specific test files
pytest test_mcp_*.py              # All MCP tests
pytest test_scripts_compile*.py   # All compilation tests
pytest test_shaders_*.py          # All shader tests

# Run specific test functions
pytest test_mcp_initialize.py::test_mcp_initialize_success
pytest -k "compile_status"        # All tests with "compile_status" in name
```

### Debugging

```bash
# Verbose output with cleanup info
pytest -v -s

# Stop on first failure
pytest -x

# Show local variables on failure
pytest -l

# Specific test with verbose output
pytest test_compile_status.py::test_compile_status_endpoint -v -s
```

## Performance Tips

### Fastest Iteration Cycles

1. **Protocol tests** (~0.4s each)
   - Pure MCP communication
   - No Unity state operations
   ```bash
   pytest -m protocol -n 2
   ```

2. **Essential tests** (~20s total, ~8s parallel)
   - Core functionality
   - Good balance of speed and coverage
   ```bash
   pytest -m essential -n 2
   ```

3. **Skip structural tests** during rapid iteration
   - Structural tests are slowest (file operations)
   ```bash
   pytest -m "not structural" -n auto
   ```

### Test Organization

- **Protocol tests** (`@pytest.mark.protocol`): No Unity cleanup needed
- **Structural tests** (`@pytest.mark.structural`): Full cleanup required
- **Minimal cleanup** (default): Fast for most compilation tests

## Common Test Combinations

```bash
# Quick sanity check before commit
pytest -m essential -n 2

# Full suite (comprehensive)
pytest -n auto

# Fast comprehensive (skip only very slow tests)
pytest -m "not slow" -n auto

# Test a specific feature area
pytest -m compilation -n 2
pytest -m mcp -n 2

# Randomized for finding dependencies
pytest --random-order
pytest -m essential --random-order
```

## Performance Expectations

### Serial Execution
- Protocol tests: ~15s for all
- Essential tests: ~20s
- Full suite: ~20-25 minutes

### Parallel Execution (-n auto)
- Protocol tests: ~5s for all
- Essential tests: ~8s
- Full suite: ~5-10 minutes

### Per-Test Average
- Protocol tests: ~0.4s each
- Minimal cleanup tests: ~2s each
- Structural tests: ~15s each (includes full cleanup)

## CI/CD Recommendations

```bash
# Fast PR validation (~30s)
pytest -m essential -n 2

# Comprehensive PR validation (~3-5 min)
pytest -m "not slow" -n auto

# Nightly full suite (~5-10 min)
pytest -n auto --random-order

# With coverage
pytest -m essential -n 2 --cov=. --cov-report=xml
```

## Troubleshooting Performance

### If tests are slow:

1. **Check if running in parallel**
   ```bash
   pytest -n auto  # Should be much faster
   ```

2. **Verify you're using the right markers**
   ```bash
   pytest -m essential  # Not pytest test_*.py
   ```

3. **Check Unity is responsive**
   - Ensure Unity Editor is running
   - Check Unity Console for errors
   - Restart Unity if needed

4. **Use test durations to find slow tests**
   ```bash
   pytest --durations=10  # Show 10 slowest tests
   ```

### If parallel execution fails:

1. **Start with fewer workers**
   ```bash
   pytest -n 2  # Instead of -n auto
   ```

2. **Check for port conflicts**
   - Multiple Unity instances need unique ports
   - See main README for multi-project setup

3. **Verify test independence**
   ```bash
   pytest --random-order  # Tests should pass in any order
   ```
