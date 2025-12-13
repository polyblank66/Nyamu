# Nyamu

**Nyamu** is minimal MCP server for Unity designed to give coding agents a tool to check script compilation.
Based on Yamu project from Keijiro Takahashi.

## Compatibility with Coding Agents and Tools

| Tool                 | Result | Tool Version               | Nyamu Version | Test Date  | Notes                                            |
|----------------------|--------|----------------------------|---------------|------------|--------------------------------------------------|
| Claude Code          | ✅      | 2.0.69                     | 0.1.0         | 2025-12-13 | Best compatibility observed since the early days |
| Zed                  | ✅      | 0.216.1                    | 0.1.0         | 2025-12-13 |                                                  |
| Rider + AI Assistant | ✅      | 2025.3.0.4 + 253.28294.360 | 0.1.0         | 2025-12-13 |                                                  |
| Rider + Junie        | ❌      | 2025.3.0.4 + 253.549.29    | 0.1.0         | 2025-12-13 | Error in the settings for the yamu tool          |
| Codex                | ❌      | 0.72.0                     | 0.1.0         | 2025-12-13 | Tool is not visible via the `/mcp` command       |

## Installation

### Install via Unity Package Manager (Git URL)

1. Open Unity Editor
2. Open **Window → Package Manager**
3. Click the **+** button in the top-left corner
4. Select **Add package from git URL...**
5. Enter the following URL:
   ```
   https://github.com/polyblank66/Nyamu.git?path=/Packages/dev.polyblank.nyamu
   ```
6. Click **Add**

Unity will install the Yamu package directly from the GitHub repository.

**Note**: Requires Unity 2021.3 or later.

## Features

- `compile_and_wait` - Triggers Unity Editor compilation, waits for completion,
  and returns compilation results including any errors
- `compile_status` - Gets current compilation status without triggering compilation.
  Returns compilation state, last compile time, and any compilation errors
- `run_tests` - Executes Unity Test Runner tests (both EditMode and PlayMode)
  with real-time status monitoring and detailed result reporting. Supports
  filtering by test names and regex patterns
- `test_status` - Gets current test execution status without running tests.
  Returns test execution state, last test time, test results, and test run ID
- `refresh_assets` - Forces Unity to refresh the asset database. Critical for
  file operations to ensure Unity detects file system changes (new/deleted/moved files)
- `editor_status` - Gets current Unity Editor status including compilation state,
  test execution state, and play mode state for real-time monitoring

## Configuration

### Response Character Limits

Nyamu provides configurable character limits for MCP server responses to prevent overwhelming AI agents with overly long responses. This is particularly useful when dealing with large compilation errors or test outputs.

**Configuration Location**: Unity Project Settings → "Nyamu MCP Server"

**Settings**:
- **Response Character Limit**: Maximum characters in complete MCP response (default: 25000)
- **Enable Truncation**: When enabled, responses exceeding the limit will be truncated
- **Truncation Message**: Message appended to indicate content was cut off

The system automatically calculates available space for content by subtracting MCP JSON overhead and truncation message length from the configured limit, ensuring maximum space is available for actual response content.

## Purpose

This proof-of-concept demonstrates how AI coding agents (Claude Code, Gemini
CLI, etc.) can autonomously iterate through edit-compile-debug cycles in Unity
development when provided with these basic compilation feedback mechanisms via
MCP.

## Prerequisites

- **Platform**: Windows (only tested platform)
- **Node.js**: Required to run the intermediate server


## Installation

### 1. Install the Package

You can install the Nyamu package (`dev.polyblank.nyamu`)...
TODO: provide git link acceptable by Unity Package Manager

### 2. Add the MCP Server to the AI Agent

You can either follow the steps in [`nyamu-mcp-setup.md`] manually, or let the
AI agent do it for you. For example, if you're using Gemini CLI:

```
You're Gemini CLI. Follow nyamu-mcp-setup.md
```

The "You're ---" statement is important, as some AI agents don't know what they
are unless explicitly told.

**Note**: You’ll need to update this configuration each time you upgrade Nyamu.
You can simply run the same prompt again to refresh it.

[`nyamu-mcp-setup.md`]: Packages/dev.polyblank.nyamu/nyamu-mcp-setup.md

