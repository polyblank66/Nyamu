## Nyamu

**Nyamu** is a minimal MCP server for Unity, designed to give coding agents a way to check script compilation. It is based on the **Yamu** project by Keijiro Takahashi.

## Compatibility with Coding Agents and Tools

| Tool                 | Result | Tool Version               | Nyamu Version | Test Date  | Notes                                            |
|----------------------|--------|----------------------------|---------------|------------|--------------------------------------------------|
| Claude Code          | ✅      | 2.0.69                     | 0.1.0         | 2025-12-13 | Best compatibility observed since the early days |
| Zed                  | ✅      | 0.216.1                    | 0.1.0         | 2025-12-13 |                                                  |
| Rider + AI Assistant | ✅      | 2025.3.0.4 + 253.28294.360 | 0.1.0         | 2025-12-13 |                                                  |
| Rider + Junie        | ❌      | 2025.3.0.4 + 253.549.29    | 0.1.0         | 2025-12-13 | Error in the settings for the nyamu tool         |
| Codex                | ❌      | 0.72.0                     | 0.1.0         | 2025-12-13 | Tool is not visible via the `/mcp` command       |

## Features

* `compile_and_wait` – Triggers Unity Editor compilation, waits for completion, and returns compilation results, including any errors.
* `compile_status` – Returns the current compilation status without triggering a compilation. Includes compilation state, last compile time, and any errors.
* `run_tests` – Executes Unity Test Runner tests (EditMode and PlayMode) with real-time status monitoring and detailed result reporting. Supports filtering by test names and regex patterns.
* `test_status` – Returns the current test execution status without running tests. Includes execution state, last test time, test results, and test run ID.
* `refresh_assets` – Forces Unity to refresh the asset database. This is critical after file operations to ensure Unity detects file system changes (new, deleted, or moved files).
* `editor_status` – Returns the current Unity Editor status, including compilation state, test execution state, and play mode state, for real-time monitoring.

## Installation

### 0. Prerequisites

* **Platform**: Windows (the only tested platform)
* **Unity**: 2021.3.45f2 or later (not a hard requirement—this is simply the version used for testing)
* **Node.js**: Required to run the intermediate server

### 1. Install the Package via Unity Package Manager (Git URL)

1. Open the Unity Editor
2. Open **Window → Package Manager**
3. Click the **+** button in the top-left corner
4. Select **Add package from git URL...**
5. Enter the following URL:

   ```
   https://github.com/polyblank66/Nyamu.git?path=/Packages/dev.polyblank.nyamu
   ```
6. Click **Add**

Unity will install the Nyamu package directly from the GitHub repository.

### 2. Add the MCP Server to the AI Agent

You can either follow the steps in [`nyamu-mcp-setup.md`] manually, or let the AI agent do it for you. For example, if you’re using Gemini CLI:

```
You're Gemini CLI. Follow nyamu-mcp-setup.md
```

The `"You're ---"` statement is important, as some AI agents do not know what they are unless explicitly told.

**Note**: You’ll need to update this configuration each time you upgrade Nyamu. You can simply run the same prompt again to refresh it.

## Configuration

### Response Character Limits

Nyamu provides configurable character limits for MCP server responses to prevent overwhelming AI agents with excessively long outputs. This is particularly useful when dealing with large compilation errors or test results.

Configuration is stored in `ProjectSettings/NyamuSettings.json` and can be shared through version control.

**Configuration Location**: Unity → Project Settings → **Nyamu MCP Server**

**Settings**:

* **Response Character Limit** – Maximum number of characters in a complete MCP response (default: 25,000)
* **Enable Truncation** – When enabled, responses exceeding the limit are truncated
* **Truncation Message** – Message appended to indicate that the content was cut off

The system automatically calculates the available space for response content by subtracting MCP JSON overhead and truncation message length from the configured limit, ensuring maximum space for meaningful output.

### Server Port Configuration

Nyamu’s HTTP server port is configurable, allowing multiple Unity Editor instances to run simultaneously without conflicts.

**Configuration Location**: Unity → Project Settings → **Nyamu MCP Server**

**Setting**:

* **Server Port** – HTTP server port (default: 17932)

#### Working with Multiple Unity Projects

To work with multiple Unity Editor instances at the same time:

1. **Configure unique ports** for each Unity project:

    * Project A: `serverPort: 17932`
    * Project B: `serverPort: 17933`

2. Provide the port settings through the `--port` argument in your coding agent’s MCP configuration.
   If you are using a global, user-level configuration for all MCP tools, define multiple tools—one per Unity project. For example:

   ```json
   {
     "mcpServers": {
       "Yamu-ProjectA": {
         "command": "node",
         "args": [
           "path/to/ProjectA/Library/PackageCache/dev.polyblank.nyamu@(HASH)/Node/mcp-server.js",
           "--port",
           "17932"
         ]
       },
       "Yamu-ProjectB": {
         "command": "node",
         "args": [
           "path/to/ProjectB/Library/PackageCache/dev.polyblank.nyamu@(HASH)/Node/mcp-server.js",
           "--port",
           "17933"
         ]
       }
     }
   }
   ```

3. **Launch all Unity Editors** — each instance will listen on its configured port.

Each AI agent session can now interact with its corresponding Unity Editor independently, enabling parallel development across multiple Unity projects.

**Important**: Port conflicts will prevent `NyamuServer` from starting. Make sure each Unity Editor uses a unique port number.


[`nyamu-mcp-setup.md`]: Packages/dev.polyblank.nyamu/nyamu-mcp-setup.md
