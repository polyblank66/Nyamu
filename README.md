# Nnyamu

**Nnyamu** (Yet Another Minimal MCP server for Unity) is an experimental MCP
(Model Context Protocol) server that enables AI coding agents to interact with
Unity projects.

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

Nnyamu provides configurable character limits for MCP server responses to prevent overwhelming AI agents with overly long responses. This is particularly useful when dealing with large compilation errors or test outputs.

**Configuration Location**: Unity Project Settings → "Nnyamu MCP Server"

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

- **Platform**: macOS (only tested platform)
- **Node.js**: Required to run the intermediate server
  ```bash
  brew install node
  ```

## Installation

### 1. Install the Package

You can install the Nnyamu package (`jp.keijiro.Nnyamu`) via the "Keijiro" scoped
registry using the Unity Package Manager. To add the registry to your project,
follow [these instructions].

[these instructions]:
  https://gist.github.com/keijiro/f8c7e8ff29bfe63d86b888901b82644c

### 2. Add the MCP Server to the AI Agent

You can either follow the steps in [`Nnyamu-mcp-setup.md`] manually, or let the
AI agent do it for you. For example, if you're using Gemini CLI:

```
You're Gemini CLI. Follow Nnyamu-mcp-setup.md
```

The "You're ---" statement is important, as some AI agents don't know what they
are unless explicitly told.

**Note**: You’ll need to update this configuration each time you upgrade Nnyamu.
You can simply run the same prompt again to refresh it.

[`Nnyamu-mcp-setup.md`]: Packages/jp.keijiro.Nnyamu/Nnyamu-mcp-setup.md
