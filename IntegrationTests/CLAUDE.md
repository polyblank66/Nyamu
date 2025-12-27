# MCP Tests - Claude Code Instructions

This directory contains Python integration tests for the NYAMU MCP (Model Context Protocol) server. These tests verify MCP functionality including compilation, test execution, and response handling.

## Running Tests

```bash
# Run all tests
cd McpTests && python -m pytest

# Run specific test categories
cd McpTests && python -m pytest -m protocol    # Fast protocol tests
cd McpTests && python -m pytest -m essential   # Core functionality tests
```

## Test Prerequisites

1. **Unity Editor** running with NYAMU project open
2. **Python 3.7+** with pytest (`pip install -r requirements.txt`)
3. **Node.js** installed
4. **Unity HTTP server** accessible at `http://localhost:17932`

## Key Test Categories

- **Protocol tests** (`@pytest.mark.protocol`): MCP communication, ultra-fast
- **Structural tests** (`@pytest.mark.structural`): File modifications, full cleanup
- **Compilation tests**: Unity compilation and error handling
- **Essential tests** (`@pytest.mark.essential`): Core functionality suite

## Performance

The test suite uses a three-tier cleanup system:
- Protocol tests: ~0.4s per test (skip Unity operations)
- Minimal tests: Fast cleanup for compilation-only tests
- Structural tests: Full cleanup for file modifications

## Troubleshooting

- **Unity not responding**: Ensure Unity Editor is open with NYAMU project
- **MCP errors**: Check Node.js installation and Unity HTTP server (port 17932)
- **Test timeouts**: Increase timeout values for slow Unity compilation

See `README.md` for detailed documentation and advanced usage.
