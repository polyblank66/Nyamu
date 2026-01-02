# Integration Tests Performance Optimization Guide

## Current Performance Analysis

### Test Timing Categories (176 total tests):
1. **Protocol tests** (~60 tests): 1-2s each = ~90-120s total
2. **Compilation tests** (~40 tests): 5-6s each = ~200-240s total
3. **Structural tests** (~20 tests): 16s each = ~320s total
4. **Shader tests** (~20 tests): 16s each = ~320s total
5. **Unity test execution** (~36 tests): 5s each = ~180s total

**Estimated current total runtime**: ~20-25 minutes serially

### Key Bottlenecks Identified:
1. **Shader compilation**: 11s per test (Unity inherently slow)
2. **Structural test teardown**: 9s full cleanup with double force refresh
3. **Unity state manager setup**: 4s for complex tests (multiple refreshes)
4. **MCP client setup/teardown**: 0.3-0.5s per test × 176 = ~60-90s total overhead

## Optimization Recommendations

### 1. Parallel Execution (HIGH IMPACT - 2-4x speedup)
**Priority**: HIGH
**Effort**: LOW
**Expected speedup**: 2-4x (20min → 5-10min)

```bash
# Add to requirements.txt
pytest-xdist>=3.0.0

# Run tests in parallel
pytest -n auto  # Use all CPU cores
pytest -n 4     # Use 4 workers
```

**Implementation steps:**
- Add pytest-xdist to requirements.txt
- Test with `-n 2` first to ensure Unity/MCP handles concurrency
- Most tests are read-only (safe for parallel)
- May need to add locks for structural tests that modify files

**Risks:**
- Unity HTTP server might not handle concurrent requests well
- MCP server might have race conditions
- Need testing to verify

### 2. Session-Scoped MCP Client (MEDIUM IMPACT)
**Priority**: MEDIUM
**Effort**: MEDIUM
**Expected speedup**: Save ~60-90s total

**Current**: Each test creates new MCP client (0.3s setup + 0.2s teardown)
**Proposed**: Share MCP client across all tests

```python
@pytest_asyncio.fixture(scope="session")
async def mcp_client_session():
    """Session-scoped MCP client shared across all tests"""
    client = MCPClient()
    await client.start()
    yield client
    await client.stop()

@pytest_asyncio.fixture(scope="function")
async def mcp_client(mcp_client_session):
    """Function-scoped wrapper for compatibility"""
    # Could add per-test reset logic here if needed
    return mcp_client_session
```

**Benefits:**
- Eliminate 176 × 0.5s = 88s of overhead
- Reduce Node.js process churn

**Risks:**
- Tests must not leave MCP client in bad state
- May need cleanup between tests

### 3. Smart Cleanup System (HIGH IMPACT for structural tests)
**Priority**: HIGH
**Effort**: MEDIUM
**Expected speedup**: Save ~100-150s on structural tests

**Current**: All structural tests do full cleanup (9s)
**Proposed**: Only cleanup when test fails or modifies state

```python
@pytest_asyncio.fixture(scope="function")
async def unity_state_manager(mcp_client, request):
    manager = UnityStateManager(mcp_client)
    cleanup_level = _get_cleanup_level(request)

    # Light pre-test check
    if cleanup_level != "noop":
        await manager.assets_refresh(force=False)

    yield manager

    # Smart cleanup: only if test failed or is structural
    if request.node.rep_call.failed or cleanup_level == "full":
        await manager.ensure_clean_state(cleanup_level=cleanup_level)
    else:
        # Minimal cleanup for passing tests
        await manager.ensure_clean_state(cleanup_level="minimal")
```

**Benefits:**
- Structural tests that pass: 16s → ~6-7s (save 9s teardown)
- 20 structural tests × 9s = 180s saved

### 4. Optimize Unity State Manager Setup (MEDIUM IMPACT)
**Priority**: MEDIUM
**Effort**: LOW
**Expected speedup**: Save ~2-3s per shader/structural test

**Current**: Double force refresh in setup (4s for shader tests)
**Proposed**: Single refresh, trust Unity state

```python
async def ensure_clean_state(self, cleanup_level="full"):
    if cleanup_level == "full":
        # Single refresh instead of double
        await self.assets_refresh(force=True)
        await self._wait_for_unity_settle(1.0)
        await self.ensure_compilation_clean()
    # ... rest unchanged
```

**Benefits:**
- Save ~2s per shader test: 20 tests × 2s = 40s
- Save ~2s per structural test: 20 tests × 2s = 40s
- Total: ~80s saved

**Risks:**
- May need to increase if Unity state issues occur

### 5. Reduce Wait Times (LOW-MEDIUM IMPACT)
**Priority**: LOW
**Effort**: MEDIUM
**Expected speedup**: Save ~30-60s total

**Current**: Fixed sleep times (1-2s waits throughout)
**Proposed**: Poll with shorter intervals

```python
async def _wait_for_unity_settle(self, max_wait=2.0):
    """Poll Unity status instead of fixed wait"""
    start = time.time()
    while time.time() - start < max_wait:
        try:
            status = await self.mcp_client.editor_status()
            if status not in ["compiling", "refreshing"]:
                return
        except:
            pass
        await asyncio.sleep(0.1)
```

**Benefits:**
- Tests finish as soon as Unity ready (instead of waiting full duration)
- Could save 0.5-1s per test with waits

### 6. Test Grouping & Ordering (LOW IMPACT, HIGH VALUE)
**Priority**: MEDIUM
**Effort**: LOW
**Expected speedup**: Faster feedback, not faster total time

**Implementation:**
```bash
# pytest.ini or pyproject.toml
[tool.pytest.ini_options]
# Run fast tests first for quick feedback
addopts = "--order-scope=module"

# Custom test order groups
markers = [
    "order_0: protocol tests (run first)",
    "order_1: compilation tests",
    "order_2: structural tests (run last)",
]
```

**Benefits:**
- Get feedback from fast tests in 1-2 minutes
- Find failures earlier
- Better developer experience

### 7. Selective Test Execution (DEVELOPER WORKFLOW)
**Priority**: HIGH (for dev workflow)
**Effort**: LOW
**Expected speedup**: N/A (documentation)

**Document usage patterns:**
```bash
# Quick smoke test (protocol + essential only)
pytest -m "protocol and essential"  # ~30s

# Skip slow tests
pytest -m "not slow"  # ~10-15min instead of 20-25min

# Only test specific functionality
pytest -m "compilation"  # Only compilation tests
pytest -m "mcp"  # Only MCP protocol tests

# Run by category
pytest test_mcp_*.py  # MCP protocol tests only
pytest test_scripts_compile*.py  # Compilation tests only
```

### 8. Caching & Fixtures Optimization (FUTURE)
**Priority**: LOW
**Effort**: HIGH
**Expected speedup**: Potentially significant for development

**Ideas:**
- Cache compilation results across test runs
- Reuse Unity project state snapshots
- Implement pytest-cache for expensive operations

## Implementation Priority Order

### Phase 1: Quick Wins (1-2 hours)
1. Add pytest-xdist and test parallel execution
2. Add test execution documentation (markers, filters)
3. Reduce wait times from 2s → 1s where safe

**Expected result**: 20-25min → 8-12min

### Phase 2: Fixture Optimization (2-4 hours)
1. Implement session-scoped MCP client
2. Implement smart cleanup system
3. Optimize Unity state manager setup

**Expected result**: 8-12min → 5-8min

### Phase 3: Advanced (future)
1. Test grouping and ordering
2. Polling instead of fixed waits
3. Caching expensive operations

**Expected result**: 5-8min → 3-5min

## Measurement Plan

Before/after metrics to track:
```bash
# Baseline (current)
pytest --durations=10

# With each optimization
pytest -n auto --durations=10  # Parallel
pytest --collect-only -q | tail -1  # Test count verification
```

Track:
- Total runtime
- Average test duration
- P50, P90, P99 test durations
- Setup/teardown overhead percentage

## Risks & Mitigations

### Risk 1: Parallel execution breaks tests
**Mitigation**:
- Start with `-n 2` to test
- Add `@pytest.mark.serial` for tests that must run alone
- Use file locks for structural tests

### Risk 2: Session fixtures cause test interdependence
**Mitigation**:
- Thorough testing with `--random-order`
- Clear documentation of fixture scope
- Rollback if issues found

### Risk 3: Reduced waits cause flakiness
**Mitigation**:
- Conservative initial reductions
- Monitor test stability
- Easy to revert

## Success Criteria

- ✅ Total test suite runtime < 10 minutes (from ~20-25min)
- ✅ Protocol tests complete in < 2 minutes
- ✅ Zero increase in test flakiness
- ✅ All 176 tests still passing
- ✅ Parallel execution works reliably

## Notes

- Unity/MCP server concurrency support needs verification
- Some optimizations are complementary (parallel + smart cleanup)
- Measurement is critical - track before/after
- Start conservative, iterate based on results
