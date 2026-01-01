# MCP Server Setup Instructions for AI Agents

## For users of the dev.polyblank.nyamu package

The Nyamu package automatically generates a `.nyamu/nyamu.bat` file in your Unity project root when the editor loads. This bat file handles launching the MCP server with the correct configuration.

### Setup Steps

1. **Open your Unity project** - The `.nyamu/nyamu.bat` file will be generated automatically
2. **Add MCP server entry** to your per-project agent settings file

Example for **Claude Code** (`.mcp.json` at project root):

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

**Important:** Replace `D:\\code\\YourProject` with the absolute path to your Unity project.

### Per-Agent Configuration Locations

- **Claude Code**: `.mcp.json` at project root
- **Gemini CLI**: `.gemini/settings.json` at project root
- **Codex CLI**: `.codex/config.json` at project root

### Configuration

The bat file automatically passes the server port from your Nyamu settings (default: 17932). You can change the port in Unity's Project Settings > Nyamu MCP Server.

## For developers

When developing fixes for the Nyamu package, the setup is the same. The bat generator automatically detects whether you're using the embedded package (`Packages/dev.polyblank.nyamu/`) or the package cache version and points to the correct `mcp-server.js` file.
