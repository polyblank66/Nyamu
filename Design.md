# Design Document for a Simple MCP Server for Unity (Nnyamu)

This project implements a minimal MCP (Model Context Protocol) server for the
Unity Editor.

The purpose of this server is to allow coding agents—such as Gemini CLI or
Claude Code—to autonomously iterate on Unity C# script development by triggering
compilation and testing processes, and retrieving results directly from the
Unity Editor.

## Features

The server provides the following minimal API endpoints:

1. `compile_and_wait`: Requests Unity Editor to recompile C# scripts and
   collects the results.
2. `run_tests`: Executes tests via Unity Test Runner and collects the results.

## Design Considerations

- **Background Execution**: Since these coding agents typically operate from a
  terminal (with Unity running in the background), the server must be able to
  force recompilation even when the Unity Editor is not in focus.

- **No File Editing**: This server does *not* support editing C# scripts. File
  modifications should be handled directly by the coding agent without going
  through the MCP server.

- **Communication**: The server should expose an HTTP API to allow integration
  with tools like Claude Code or Gemini CLI. A lightweight relay server
  implemented in Node.js or Python may be used to facilitate communication.

- **Platform**: The system is intended to run on macOS. Windows support is not
  required at this stage. Simplicity and ease of implementation should be
  prioritized.

## Project Naming

The project is named **"Nnyamu"**. Use this name consistently in all filenames,
identifiers, and documentation.
