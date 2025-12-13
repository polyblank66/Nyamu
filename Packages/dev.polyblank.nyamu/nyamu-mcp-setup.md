# MCP Server Setup Instructions for AI Agents

## For users of the dev.polyblank.nyamu package

Add an MCP server entry to your **per-project agent settings file**.

Example:

```
{
  "mcpServers": {
    "Nyamu": {
      "command": "node",
      "args": ["./Library/PackageCache/dev.polyblank.nyamu@(HASH)/Node/mcp-server.js"]
    }
  }
}
```

`(HASH)` varies between versions, so you should locate it using a shell command.

The location of the **per-project agent settings file** depends on the AI agent you're using. For Claude Code, the file is usually `.mcp.json` at the project root. For Gemini CLI, the most common location is `.gemini/settings.json`. For Codex CLI usual location is `.codex/config.json`

If the file already contains a `Nyamu` entry in the `mcpServers` section, you
only need to update the `(HASH)` in the `args` field.

## For developers

When developing fixed for Nymu package within Nyamu testing project use `./Packages/dev.polyblank.nyamu/Node/mcp-server.js` as path to MCP tool
