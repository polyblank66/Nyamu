## Nyamu

**Nyamu** is a minimal MCP server for Unity, designed to give coding agents a way to check script compilation. It is based on the **Yamu** project by Keijiro Takahashi.

## Compatibility with Coding Agents and Tools

Prompt: Check scripts compilation with nyamu mcp tool

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

When you first open your Unity project with Nyamu installed, it automatically generates a `.nyamu/nyamu.bat` file. This bat file launches the MCP server with the correct configuration.

**Option 1: Manual Setup (Recommended)**

Add the bat file path to your MCP settings. For **Claude Code** (`.mcp.json`):

```json
{
  "mcpServers": {
    "Nyamu": {
      "command": "cmd.exe",
      "args": ["/c", "D:\\code\\YourProject\\.nyamu\\nyamu.bat"],
      "timeout": 30000
    }
  }
}
```

Replace `D:\\code\\YourProject` with your actual project path.

**Option 2: Let the AI Agent Configure Itself**

Ask your AI agent to follow the setup instructions. For example, if you're using Gemini CLI:

```
You're Gemini CLI. Follow nyamu-mcp-setup.md
```

The `"You're ---"` statement is important, as some AI agents do not know what they are unless explicitly told.

For detailed setup instructions for different AI agents, see [`nyamu-mcp-setup.md`].

**Note**: The bat file automatically updates when you change Nyamu settings (like the server port), so you typically only need to configure this once.

## Configuration

### Response Character Limits

Nyamu provides configurable character limits for MCP server responses to prevent overwhelming AI agents with excessively long outputs. This is particularly useful when dealing with large compilation errors or test results.

Configuration is stored in `.nyamu/NyamuSettings.json` and can be edited manually or through Unity's Project Settings UI.

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

    * Project A: Unity → Project Settings → Nyamu MCP Server → Server Port: `17932`
    * Project B: Unity → Project Settings → Nyamu MCP Server → Server Port: `17933`

2. Each project will automatically generate its own bat file with the correct port. Configure your AI agent with multiple MCP entries:

   ```json
   {
     "mcpServers": {
       "Nyamu-ProjectA": {
         "command": "cmd.exe",
         "args": ["/c", "D:\\code\\ProjectA\\.nyamu\\nyamu.bat"],
         "timeout": 30000
       },
       "Nyamu-ProjectB": {
         "command": "cmd.exe",
         "args": ["/c", "D:\\code\\ProjectB\\.nyamu\\nyamu.bat"],
         "timeout": 30000
       }
     }
   }
   ```

3. **Launch all Unity Editors** — each instance will listen on its configured port.

Each AI agent session can now interact with its corresponding Unity Editor independently, enabling parallel development across multiple Unity projects.

**Important**: Port conflicts will prevent `NyamuServer` from starting. Make sure each Unity Editor uses a unique port number.

**Note**: The bat files automatically include the `--port` parameter from each project's settings, so you don't need to specify it manually in the MCP configuration.


[`nyamu-mcp-setup.md`]: Packages/dev.polyblank.nyamu/nyamu-mcp-setup.md
