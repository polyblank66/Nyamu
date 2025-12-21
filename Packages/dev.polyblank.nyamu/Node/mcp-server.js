#!/usr/bin/env node

const http = require('http');
const fs = require('fs');
const path = require('path');

// File-based logger for MCP server
// CRITICAL: Never log to stdout/stderr - MCP protocol uses stdio
class FileLogger {
    constructor(logFilePath) {
        this.logFilePath = logFilePath;
        this.enabled = false;
        this.writeQueue = [];
        this.writing = false;

        if (logFilePath) {
            try {
                // Ensure log directory exists
                const logDir = path.dirname(logFilePath);
                if (!fs.existsSync(logDir)) {
                    fs.mkdirSync(logDir, { recursive: true });
                }

                // Create or truncate log file
                fs.writeFileSync(logFilePath, `=== Nyamu MCP Server Log Started: ${new Date().toISOString()} ===\n`, 'utf8');
                this.enabled = true;
            } catch (error) {
                // Silently disable logging on errors - never crash the server
                this.enabled = false;
            }
        }
    }

    log(level, message, data = null) {
        if (!this.enabled) return;

        const timestamp = new Date().toISOString();
        let logLine = `[${timestamp}] [${level}] ${message}`;

        if (data !== null && data !== undefined) {
            try {
                // Pretty-print objects for readability
                const dataStr = typeof data === 'string' ? data : JSON.stringify(data, null, 2);
                logLine += `\n${dataStr}`;
            } catch (error) {
                logLine += `\n[Error serializing data: ${error.message}]`;
            }
        }

        logLine += '\n';
        this.writeQueue.push(logLine);
        this.processQueue();
    }

    async processQueue() {
        if (this.writing || this.writeQueue.length === 0) return;

        this.writing = true;
        try {
            const batch = this.writeQueue.splice(0, 10);
            const content = batch.join('');
            await fs.promises.appendFile(this.logFilePath, content, 'utf8');
        } catch (error) {
            // Disable logging on write errors
            this.enabled = false;
            this.writeQueue = [];
        } finally {
            this.writing = false;
            if (this.writeQueue.length > 0) {
                setImmediate(() => this.processQueue());
            }
        }
    }

    logRequest(method, params, id) {
        if (!this.enabled) return;
        this.log('REQUEST', `Method: ${method}, ID: ${id}`, { method, params, id, timestamp: Date.now() });
    }

    logResponse(response) {
        if (!this.enabled) return;
        const level = response.error ? 'ERROR' : 'RESPONSE';
        const message = response.error ? `Error response for ID: ${response.id}` : `Success response for ID: ${response.id}`;
        this.log(level, message, response);
    }

    logError(context, error) {
        if (!this.enabled) return;
        this.log('ERROR', context, { message: error.message, stack: error.stack, code: error.code, data: error.data });
    }

    logInfo(message, data = null) {
        if (!this.enabled) return;
        this.log('INFO', message, data);
    }
}

let logger = null;

// Gracefully handle broken pipe when the host (e.g., Rider) closes the MCP stdio pipe.
// Without this, Node will emit an unhandled 'error' on process.stdout and crash with EPIPE.
(function setupStdoutSafety() {
    try {
        if (process && process.stdout && typeof process.stdout.on === 'function') {
            process.stdout.on('error', (err) => {
                const code = err && err.code;
                if (code === 'EPIPE' || code === 'ECONNRESET' || code === 'ERR_STREAM_WRITE_AFTER_END') {
                    // Host disconnected; exit quietly without writing to stderr
                    try { process.exit(0); } catch (_) {}
                }
                // For other errors, do nothing special to avoid recursive writes.
            });
        }
    } catch (_) { /* ignore */ }

    // Provide a safe write that won't throw if stdout is gone.
    global.safeStdoutWrite = function(data) {
        try {
            if (process && process.stdout && !process.stdout.writableEnded) {
                process.stdout.write(data);
            }
        } catch (_) {
            // Swallow to avoid crashes when the pipe is closed.
        }
    };
})();

// Custom error classes for Unity-specific issues
class UnityUnavailableError extends Error {
    constructor(message, data) {
        super(message);
        this.name = 'UnityUnavailableError';
        this.data = data;
    }
}

class UnityRestartingError extends Error {
    constructor(message, data) {
        super(message);
        this.name = 'UnityRestartingError';
        this.data = data;
    }
}

// Configuration manager for loading Unity settings via HTTP endpoint
class ConfigManager {
    constructor(unityServerUrl) {
        this.unityServerUrl = unityServerUrl;
        this.config = null;
        this.lastFetchTime = 0;
        this.cacheExpiry = 30000; // Cache for 30 seconds
    }

    async getConfiguration() {
        const now = Date.now();

        // Return cached config if still valid
        if (this.config && (now - this.lastFetchTime) < this.cacheExpiry) {
            return this.config;
        }

        try {
            // Try to fetch settings from Unity
            const response = await this.makeHttpRequest('/mcp-settings');
            this.config = response;
            this.lastFetchTime = now;
            return this.config;
        } catch (error) {
            // Unity not available or error occurred, use defaults
            if (!this.config) {
                this.config = this.getDefaultConfiguration();
            }
            return this.config;
        }
    }

    getDefaultConfiguration() {
        return {
            responseCharacterLimit: 25000,
            enableTruncation: true,
            truncationMessage: "\n\n... (response truncated due to length limit)"
        };
    }

    async makeHttpRequest(path) {
        return new Promise((resolve, reject) => {
            const http = require('http');
            const req = http.request(`${this.unityServerUrl}${path}`, { method: 'GET' }, (res) => {
                let data = '';
                res.on('data', chunk => data += chunk);
                res.on('end', () => {
                    try {
                        const parsed = JSON.parse(data);
                        resolve(parsed);
                    } catch (error) {
                        reject(new Error(`Invalid JSON response: ${data}`));
                    }
                });
            });

            req.on('error', (error) => {
                reject(error);
            });

            req.setTimeout(5000, () => {
                req.destroy();
                reject(new Error('Request timeout'));
            });

            req.end();
        });
    }
}

// Response formatter that applies character limits and smart truncation
class ResponseFormatter {
    constructor(config) {
        this.config = config;
        this.calculateAvailableSpace();
    }

    updateConfig(config) {
        this.config = config;
        this.calculateAvailableSpace();
    }

    calculateAvailableSpace() {
        if (!this.config.enableTruncation) {
            this.availableContentSpace = Infinity;
            return;
        }

        // Calculate overhead of MCP response structure
        const sampleResponse = JSON.stringify({
            jsonrpc: '2.0',
            id: 999999,
            result: {
                content: [{
                    type: 'text',
                    text: ''
                }]
            }
        });

        const mcpOverhead = sampleResponse.length;
        const truncationOverhead = this.config.truncationMessage.length;
        const buffer = 50; // Safety buffer

        this.availableContentSpace = Math.max(
            1000, // Minimum space for content
            this.config.responseCharacterLimit - mcpOverhead - truncationOverhead - buffer
        );
    }

    formatResponse(content) {
        if (!this.config.enableTruncation || content.length <= this.availableContentSpace) {
            return content;
        }

        // Smart truncation - try to preserve important information
        let truncated = content.substring(0, this.availableContentSpace);

        // Try to truncate at word boundary if possible
        const lastSpaceIndex = truncated.lastIndexOf(' ');
        const lastNewlineIndex = truncated.lastIndexOf('\n');
        const breakPoint = Math.max(lastSpaceIndex, lastNewlineIndex);

        if (breakPoint > this.availableContentSpace * 0.8) { // Only use break point if it's not too far back
            truncated = content.substring(0, breakPoint);
        }

        return truncated + this.config.truncationMessage;
    }

    // Smart truncation for JSON responses - preserve structure when possible
    formatJsonResponse(jsonObj) {
        const jsonString = JSON.stringify(jsonObj);

        if (!this.config.enableTruncation || jsonString.length <= this.availableContentSpace) {
            return jsonString;
        }

        // For JSON responses, try to preserve the most important parts
        if (jsonObj.errors && Array.isArray(jsonObj.errors)) {
            // For compilation errors, show first few errors
            const truncatedObj = { ...jsonObj };
            const errorLimit = Math.floor(this.availableContentSpace / 200); // Estimate ~200 chars per error
            truncatedObj.errors = jsonObj.errors.slice(0, Math.max(1, errorLimit));

            if (jsonObj.errors.length > truncatedObj.errors.length) {
                truncatedObj.errors.push({
                    file: '...',
                    line: 0,
                    message: `... and ${jsonObj.errors.length - truncatedObj.errors.length} more errors (truncated)`
                });
            }

            return JSON.stringify(truncatedObj);
        }

        // For other JSON, fall back to string truncation
        return this.formatResponse(jsonString);
    }
}

class MCPServer {
    constructor(port = 17932) {
        this.unityServerUrl = `http://localhost:${port}`;
        this.configManager = new ConfigManager(this.unityServerUrl);
        this.responseFormatter = null; // Will be initialized after first config load
        this.stdinBuffer = Buffer.alloc(0);
        this.activeProtocol = null; // 'content-length' | 'newline'
        this.capabilities = {
            tools: {
                compilation_trigger: {
                    description: "Request Unity Editor to compile C# scripts and wait for completion. Returns compilation status and any errors. IMPORTANT: For structural changes (new/deleted/moved files), call refresh_assets first (use force=true for deletions), wait for MCP responsiveness, then call this tool. Without refresh, Unity may not detect file changes. WHEN EDITING EXISTING FILES: Call compilation_trigger directly without refresh. LLM HINTS: If you get Error -32603 with 'Unity HTTP server restarting', this is normal during compilation - wait 3-5 seconds and retry. If you get 'Unity Editor HTTP server unavailable', verify Unity Editor is running with NYAMU project open.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            timeout: {
                                type: "number",
                                description: "Timeout in seconds (default: 30). LLM HINT: Use longer timeouts (45-60s) for large projects or complex compilation tasks.",
                                default: 30
                            }
                        },
                        required: []
                    }
                },
                tests_run_single: {
                    description: "Execute a single specific Unity test by its full name. Returns test results including pass/fail status and detailed failure information. Supports both EditMode and PlayMode execution. LLM HINTS: Use for running individual tests, ideal for focused testing of specific functionality.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            test_name: {
                                type: "string",
                                description: "Full name of the test to run (required). The full name of the test including namespace and fixture. Format: Namespace.FixtureName.TestName. Example: MyProject.Tests.PlayerControllerTests.TestJump",
                                default: ""
                            },
                            test_mode: {
                                type: "string",
                                description: "Test mode: EditMode or PlayMode (default: EditMode). LLM HINT: Use EditMode for quick verification, PlayMode for comprehensive runtime testing.",
                                enum: ["EditMode", "PlayMode"],
                                default: "EditMode"
                            },
                            timeout: {
                                type: "number",
                                description: "Timeout in seconds (default: 60). LLM HINT: PlayMode tests typically need 60-120s, EditMode tests usually complete within 30s.",
                                default: 60
                            }
                        },
                        required: ["test_name"]
                    }
                },
                tests_run_all: {
                    description: "Execute all Unity tests in the specified mode. Returns comprehensive test results including pass/fail counts and detailed failure information. Supports both EditMode and PlayMode execution. LLM HINTS: Use for complete test suite execution, ideal for CI/CD pipelines and comprehensive verification.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            test_mode: {
                                type: "string",
                                description: "Test mode: EditMode or PlayMode (default: EditMode). LLM HINT: Use EditMode for quick verification, PlayMode for comprehensive runtime testing.",
                                enum: ["EditMode", "PlayMode"],
                                default: "EditMode"
                            },
                            timeout: {
                                type: "number",
                                description: "Timeout in seconds (default: 60). LLM HINT: PlayMode tests typically need 60-120s, EditMode tests usually complete within 30s.",
                                default: 60
                            }
                        },
                        required: []
                    }
                },
                tests_run_regex: {
                    description: "Execute Unity tests matching a regex pattern. Returns test results including pass/fail counts and detailed failure information. Supports both EditMode and PlayMode execution. LLM HINTS: Use for flexible test selection by patterns, ideal for running test suites by namespace, category, or naming conventions.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            test_filter_regex: {
                                type: "string",
                                description: "Regex pattern for filtering tests (required). Use .NET Regex syntax to match test names by pattern. Example patterns: '.*PlayerController.*' for all PlayerController tests, 'Tests\\\\.Movement\\\\..*' for all tests in Movement namespace, '.*(Jump|Run).*' for tests containing Jump or Run.",
                                default: ""
                            },
                            test_mode: {
                                type: "string",
                                description: "Test mode: EditMode or PlayMode (default: EditMode). LLM HINT: Use EditMode for quick verification, PlayMode for comprehensive runtime testing.",
                                enum: ["EditMode", "PlayMode"],
                                default: "EditMode"
                            },
                            timeout: {
                                type: "number",
                                description: "Timeout in seconds (default: 60). LLM HINT: PlayMode tests typically need 60-120s, EditMode tests usually complete within 30s.",
                                default: 60
                            }
                        },
                        required: ["test_filter_regex"]
                    }
                },
                refresh_assets: {
                    description: "Force Unity to refresh the asset database. CRITICAL for file operations - Unity may not detect file system changes without this. Regular refresh works for new files, but force=true is required for deletions to prevent CS2001 'Source file could not be found' errors. Workflow: 1) Make file changes, 2) Call refresh_assets (force=true for deletions), 3) Wait for MCP responsiveness, 4) Call compile_and_wait. LLM HINTS: Always call this after creating/deleting/moving files in Unity project. Unity HTTP server will restart during refresh - expect temporary -32603 errors that resolve automatically.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            force: {
                                type: "boolean",
                                description: "Use ImportAssetOptions.ForceUpdate for stronger refresh. Set to true when deleting files to prevent Unity CS2001 errors. False is sufficient for new file creation. LLM HINT: Use force=true when deleting files, force=false when creating new files.",
                                default: false
                            }
                        },
                        required: []
                    }
                },
                editor_status: {
                    description: "Get current Unity Editor status including compilation state, test execution state, and play mode state. Returns real-time information about what the editor is currently doing.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                        required: []
                    }
                },
                compilation_status: {
                    description: "Get current compilation status without triggering compilation. Returns compilation state, last compile time, and any compilation errors.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                        required: []
                    }
                },
                tests_status: {
                    description: "Get current test execution status without running tests. Returns test execution state, last test time, test results, and test run ID.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                        required: []
                    }
                },
                tests_cancel: {
                    description: "Cancel running Unity test execution. Uses Unity's TestRunnerApi.CancelTestRun(guid). Currently only supports EditMode tests. If no guid is provided, cancels the current test run. LLM HINTS: Only EditMode tests can be cancelled. PlayMode test cancellation is not supported by Unity's API.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            test_run_guid: {
                                type: "string",
                                description: "GUID of the test run to cancel (optional). If not provided, cancels the current running test. The GUID is typically returned when starting a test run.",
                                default: ""
                            }
                        },
                        required: []
                    }
                },
                compile_shader: {
                    description: "Compile a single shader with fuzzy name matching. Shows all matches but auto-compiles best match. Returns compilation errors/warnings and time. LLM HINTS: Fuzzy search supported - exact names not required.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            shader_name: {
                                type: "string",
                                description: "Shader name to search for (fuzzy matching). Example: 'Standard', 'unlit', 'custom/myshader'"
                            },
                            timeout: {
                                type: "number",
                                description: "Timeout in seconds (default: 30)",
                                default: 30
                            }
                        },
                        required: ["shader_name"]
                    }
                },
                compile_all_shaders: {
                    description: "Compile all shaders in Unity project. Returns per-shader results with errors/warnings. WARNING: Can take 15+ minutes or much longer, especially for URP projects. Strongly consider using compile_shaders_regex instead. LLM HINTS: Avoid this tool unless absolutely necessary. Use compile_shader or compile_shaders_regex for targeted compilation.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            timeout: {
                                type: "number",
                                description: "Timeout in seconds (default: 120)",
                                default: 120
                            }
                        },
                        required: []
                    }
                },
                compile_shaders_regex: {
                    description: "Compile shaders matching a regex pattern applied to shader file paths. Returns per-shader results with errors/warnings. Supports MCP progress notifications when progressToken is provided in request _meta. LLM HINTS: Use this to compile a subset of shaders based on path patterns. Progress notifications sent roughly every 500ms during compilation.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            pattern: {
                                type: "string",
                                description: "Regex pattern to match against shader file paths. Example: '.*Standard.*', 'Assets/Shaders/Custom/.*'"
                            },
                            timeout: {
                                type: "number",
                                description: "Timeout in seconds (default: 120)",
                                default: 120
                            }
                        },
                        required: ["pattern"]
                    }
                },
                shader_compilation_status: {
                    description: "Get current shader compilation status without triggering compilation. Returns whether shaders are compiling, last compilation type (single/all/regex), last compilation time, and complete results from the previous shader compilation command. LLM HINTS: Always check this before calling compile_shader/compile_all_shaders/compile_shaders_regex to avoid redundant compilations.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                        required: []
                    }
                },
                editor_log_path: {
                    description: "Get Unity Editor log file path. Returns the platform-specific path to the Unity Editor log file along with existence status. LLM HINTS: Use this to verify log file location before reading. Log path is platform-dependent (Windows: %LOCALAPPDATA%/Unity/Editor/Editor.log, Mac: ~/Library/Logs/Unity/Editor.log, Linux: ~/.config/unity3d/Editor.log). The log file contains all Unity Editor output including compilation errors, warnings, and debug messages.",
                    inputSchema: {
                        type: "object",
                        properties: {},
                        required: []
                    }
                },
                editor_log_head: {
                    description: "Read first N lines from Unity Editor log file. Returns the beginning of the log file, useful for session startup information and initialization logs. LLM HINTS: Use this to see Unity startup sequence, package loading, and early initialization messages. Default line count is 100. Supports filtering by log type (error, warning, info) to quickly find specific issues during startup.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            line_count: {
                                type: "number",
                                description: "Number of lines to read from the beginning (default: 100, max: 10000). LLM HINT: Use 100-500 for quick checks, 1000+ for detailed startup analysis.",
                                default: 100
                            },
                            log_type: {
                                type: "string",
                                description: "Filter by log type: 'all', 'error', 'warning', 'info' (default: 'all'). LLM HINT: Use 'error' to quickly identify startup errors, 'warning' for potential issues.",
                                enum: ["all", "error", "warning", "info"],
                                default: "all"
                            }
                        },
                        required: []
                    }
                },
                editor_log_tail: {
                    description: "Read last N lines from Unity Editor log file. Returns the most recent log entries, useful for debugging current Unity Editor state and recent operations. LLM HINTS: Use this to see latest compilation errors, recent warnings, or current Unity state. Default line count is 100. Supports filtering by log type to quickly isolate errors or warnings from recent activity.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            line_count: {
                                type: "number",
                                description: "Number of lines to read from the end (default: 100, max: 10000). LLM HINT: Use 100-200 for recent activity, 500+ for broader context.",
                                default: 100
                            },
                            log_type: {
                                type: "string",
                                description: "Filter by log type: 'all', 'error', 'warning', 'info' (default: 'all'). LLM HINT: Use 'error' for recent errors, 'warning' for recent issues.",
                                enum: ["all", "error", "warning", "info"],
                                default: "all"
                            }
                        },
                        required: []
                    }
                },
                editor_log_grep: {
                    description: "Search Unity Editor log for lines matching a pattern. Returns matching log lines with optional context. Supports case-sensitive/insensitive search. LLM HINTS: Use this to find specific errors, warnings, or debug messages. Supports JavaScript regex patterns. Can return large results - consider using line_limit. Combine pattern matching with log_type filter for precise results.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            pattern: {
                                type: "string",
                                description: "Search pattern (JavaScript regex). Example: 'error', 'NyamuServer.*error', 'Shader.*failed'. LLM HINT: Use specific patterns to narrow results, case-insensitive by default."
                            },
                            case_sensitive: {
                                type: "boolean",
                                description: "Enable case-sensitive search (default: false). LLM HINT: Keep false for most searches.",
                                default: false
                            },
                            context_lines: {
                                type: "number",
                                description: "Number of context lines to show before and after each match (default: 0, max: 10). LLM HINT: Use 2-5 for stack traces and error context.",
                                default: 0
                            },
                            line_limit: {
                                type: "number",
                                description: "Maximum number of matching lines to return (default: 1000, max: 10000). LLM HINT: Use lower values (100-500) to avoid overwhelming responses.",
                                default: 1000
                            },
                            log_type: {
                                type: "string",
                                description: "Filter by log type: 'all', 'error', 'warning', 'info' (default: 'all'). LLM HINT: Combine with pattern for precise filtering.",
                                enum: ["all", "error", "warning", "info"],
                                default: "all"
                            }
                        },
                        required: ["pattern"]
                    }
                },
                execute_menu_item: {
                    description: "Execute Unity Editor menu item by path. Triggers any Unity menu command programmatically. LLM HINTS: Use full menu path like 'Assets/Create/C# Script' or 'GameObject/Create Empty'. Returns success/failure status. Useful for automating Unity Editor operations.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            menu_item_path: {
                                type: "string",
                                description: "Full path to menu item (e.g., 'Assets/Create/C# Script', 'GameObject/Create Empty', 'Edit/Project Settings...'). LLM HINT: Use exact path as shown in Unity Editor menus."
                            }
                        },
                        required: ["menu_item_path"]
                    }
                }
            }
        };
    }

    async handleRequest(request) {
        const { method, params, id } = request;

        switch (method) {
            case 'initialize':
                const clientVersion = params?.protocolVersion;
                if (!clientVersion) {
                    return {
                        jsonrpc: '2.0',
                        id,
                        error: {
                            code: -32602,
                            message: 'Invalid params: protocolVersion is required'
                        }
                    };
                }

                return {
                    jsonrpc: '2.0',
                    id,
                    result: {
                        protocolVersion: '2024-11-05',
                        capabilities: this.capabilities,
                        serverInfo: {
                            name: 'NyamuServer',
                            version: '1.0.0'
                        }
                    }
                };

            case 'tools/list':
                return {
                    jsonrpc: '2.0',
                    id,
                    result: {
                        tools: Object.entries(this.capabilities.tools).map(([name, tool]) => ({
                            name,
                            description: tool.description,
                            inputSchema: tool.inputSchema
                        }))
                    }
                };

            case 'tools/call':
                return await this.handleToolCall(params, id);

            default:
                return {
                    jsonrpc: '2.0',
                    id,
                    error: {
                        code: -32601,
                        message: 'Method not found'
                    }
                };
        }
    }

    async ensureResponseFormatter() {
        if (!this.responseFormatter) {
            const config = await this.configManager.getConfiguration();
            this.responseFormatter = new ResponseFormatter(config);
        } else {
            // Update formatter with latest config if needed
            const config = await this.configManager.getConfiguration();
            this.responseFormatter.updateConfig(config);
        }
    }

    sendProgressNotification(progressToken, progress, total, message) {
        const notification = {
            jsonrpc: '2.0',
            method: 'notifications/progress',
            params: {
                progressToken: progressToken,
                progress: progress,
                total: total
            }
        };

        if (message) {
            notification.params.message = message;
        }

        // Send notification using existing sendJsonResponse method
        // Notifications don't have an 'id' field (only 'method' and 'params')
        this.sendJsonResponse(notification, this.activeProtocol);
    }

    async handleToolCall(params, id) {
        const { name, arguments: args, _meta } = params;
        const progressToken = _meta?.progressToken || null;

        try {
            switch (name) {
                case 'compilation_trigger':
                    return await this.callCompileAndWait(id, args.timeout || 30);
                case 'tests_run_single':
                    return await this.callTestsRunSingle(id, args.test_name, args.test_mode || 'EditMode', args.timeout || 60);
                case 'tests_run_all':
                    return await this.callTestsRunAll(id, args.test_mode || 'EditMode', args.timeout || 60);
                case 'tests_run_regex':
                    return await this.callTestsRunRegex(id, args.test_filter_regex, args.test_mode || 'EditMode', args.timeout || 60);
                case 'refresh_assets':
                    return await this.callRefreshAssets(id, args.force || false);
                case 'editor_status':
                    return await this.callEditorStatus(id);
                case 'compilation_status':
                    return await this.callCompileStatus(id);
                case 'tests_status':
                    return await this.callTestStatus(id);
                case 'tests_cancel':
                    return await this.callTestsCancel(id, args.test_run_guid || '');
                case 'compile_shader':
                    return await this.callCompileShader(id, args.shader_name, args.timeout || 30);
                case 'compile_all_shaders':
                    return await this.callCompileAllShaders(id, args.timeout || 120);
                case 'compile_shaders_regex':
                    return await this.callCompileShadersRegex(id, args.pattern, args.timeout || 120, progressToken);
                case 'shader_compilation_status':
                    return await this.callShaderCompilationStatus(id);
                case 'editor_log_path':
                    return await this.callEditorLogPath(id);
                case 'editor_log_head':
                    return await this.callEditorLogHead(id, args.line_count || 100, args.log_type || 'all');
                case 'editor_log_tail':
                    return await this.callEditorLogTail(id, args.line_count || 100, args.log_type || 'all');
                case 'editor_log_grep':
                    return await this.callEditorLogGrep(id, args.pattern, args.case_sensitive || false, args.context_lines || 0, args.line_limit || 1000, args.log_type || 'all');
                case 'execute_menu_item':
                    return await this.callExecuteMenuItem(id, args.menu_item_path);
                default:
                    return {
                        jsonrpc: '2.0',
                        id,
                        error: {
                            code: -32602,
                            message: `Unknown tool: ${name}`
                        }
                    };
            }
        } catch (error) {
            // Enhanced error handling with LLM-friendly instructions
            if (error instanceof UnityUnavailableError || error instanceof UnityRestartingError) {
                return {
                    jsonrpc: '2.0',
                    id,
                    error: {
                        code: -32603,
                        message: error.message,
                        data: error.data
                    }
                };
            } else {
                // Generic error fallback
                return {
                    jsonrpc: '2.0',
                    id,
                    error: {
                        code: -32603,
                        message: `Tool execution failed: ${error.message}`
                    }
                };
            }
        }
    }

    async callCompileAndWait(id, timeoutSeconds) {
        try {
            // Ensure response formatter is ready
            await this.ensureResponseFormatter();

            // Start compilation
            const compileResponse = await this.makeHttpRequest('/compilation-trigger');

            // C# side now ensures compilation has started, so we can immediately begin polling

            // Wait for completion with polling
            const startTime = Date.now();
            const timeoutMs = timeoutSeconds * 1000;

            // Wait for compilation to complete
            while (Date.now() - startTime < timeoutMs) {
                try {
                    const statusResponse = await this.makeHttpRequest('/compilation-status');

                    if (statusResponse.status === 'idle') {

                        // Compilation completed, errors are included in status response
                        const errorText = statusResponse.errors && statusResponse.errors.length > 0
                            ? `Compilation completed with errors:\n${statusResponse.errors.map(err => `${err.file}:${err.line} - ${err.message}`).join('\n')}`
                            : 'Compilation completed successfully with no errors.';

                        // Apply response formatting
                        const formattedText = this.responseFormatter.formatResponse(errorText);

                        return {
                            jsonrpc: '2.0',
                            id,
                            result: {
                                content: [{
                                    type: 'text',
                                    text: formattedText
                                }]
                            }
                        };
                    }

                    // Wait 1 second before next poll
                    await new Promise(resolve => setTimeout(resolve, 1000));
                } catch (pollError) {
                    // Continue polling despite individual request failures
                    await new Promise(resolve => setTimeout(resolve, 2000));
                    continue;
                }
            }

            // Timeout reached
            throw new Error(`Compilation timeout after ${timeoutSeconds} seconds`);

        } catch (error) {
            throw new Error(`Failed to compile and wait: ${error.message}`);
        }
    }

    async callTestsRunSingle(id, testName, testMode, timeoutSeconds) {
        try {
            // Ensure response formatter is ready
            await this.ensureResponseFormatter();

            // Get initial status to capture current test run ID (if any)
            const initialStatus = await this.makeHttpRequest('/tests-status');
            const initialTestRunId = initialStatus.testRunId;

            // Start single test execution
            const runResponse = await this.makeHttpRequest(`/tests-run-single?test_name=${encodeURIComponent(testName)}&mode=${testMode}`);

            // Wait for test execution to actually start and get new test run ID
            const startTime = Date.now();
            const timeoutMs = timeoutSeconds * 1000;
            let currentTestRunId = initialTestRunId;

            // First, wait for test execution to start (new test run ID)
            let testStarted = false;
            const startCheckTimeout = 10000; // 10 seconds timeout for test start

            while (Date.now() - startTime < startCheckTimeout) {
                try {
                    const statusResponse = await this.makeHttpRequest('/tests-status');

                    // Check for early error detection
                    if (statusResponse.hasError && statusResponse.errorMessage) {
                        throw new Error(`Test execution failed to start: ${statusResponse.errorMessage}`);
                    }

                    if (statusResponse.testRunId && statusResponse.testRunId !== initialTestRunId) {
                        currentTestRunId = statusResponse.testRunId;
                        testStarted = true;
                        break;
                    }

                    // Wait 200ms before next poll for test start
                    await new Promise(resolve => setTimeout(resolve, 200));
                } catch (pollError) {
                    await new Promise(resolve => setTimeout(resolve, 500));
                    continue;
                }
            }

            if (!testStarted) {
                throw new Error('Test execution failed to start - no new test run ID detected');
            }

            // Now wait for completion with the specific test run ID
            while (Date.now() - startTime < timeoutMs) {
                try {
                    const statusResponse = await this.makeHttpRequest('/tests-status');

                    // Check for errors during test execution
                    if (statusResponse.hasError && statusResponse.errorMessage) {
                        throw new Error(`Test execution error: ${statusResponse.errorMessage}`);
                    }

                    // Check if this is the same test run and it's completed
                    if (statusResponse.testRunId === currentTestRunId && statusResponse.status === 'idle') {
                        // Test execution completed for our specific test run
                        const resultText = this.formatTestResults(statusResponse);

                        // Apply response formatting
                        const formattedText = this.responseFormatter.formatResponse(resultText);

                        return {
                            jsonrpc: '2.0',
                            id,
                            result: {
                                content: [{
                                    type: 'text',
                                    text: formattedText
                                }]
                            }
                        };
                    }

                    // Wait 1 second before next poll
                    await new Promise(resolve => setTimeout(resolve, 1000));
                } catch (pollError) {
                    // Continue polling despite individual request failures
                    await new Promise(resolve => setTimeout(resolve, 2000));
                    continue;
                }
            }

            // Timeout reached
            throw new Error(`Test execution timeout after ${timeoutSeconds} seconds`);

        } catch (error) {
            throw new Error(`Failed to run tests: ${error.message}`);
        }
    }

    async callTestsRunAll(id, testMode, timeoutSeconds) {
        try {
            // Ensure response formatter is ready
            await this.ensureResponseFormatter();

            // Get initial status to capture current test run ID (if any)
            const initialStatus = await this.makeHttpRequest('/tests-status');
            const initialTestRunId = initialStatus.testRunId;

            // Start all tests execution
            const runResponse = await this.makeHttpRequest(`/tests-run-all?mode=${testMode}`);

            // Wait for test execution to actually start and get new test run ID
            const startTime = Date.now();
            const timeoutMs = timeoutSeconds * 1000;
            let currentTestRunId = initialTestRunId;

            // First, wait for test execution to start (new test run ID)
            let testStarted = false;
            const startCheckTimeout = 10000; // 10 seconds timeout for test start

            while (Date.now() - startTime < startCheckTimeout) {
                try {
                    const statusResponse = await this.makeHttpRequest('/tests-status');

                    // Check for early error detection
                    if (statusResponse.hasError && statusResponse.errorMessage) {
                        throw new Error(`Test execution failed to start: ${statusResponse.errorMessage}`);
                    }

                    if (statusResponse.testRunId && statusResponse.testRunId !== initialTestRunId) {
                        currentTestRunId = statusResponse.testRunId;
                        testStarted = true;
                        break;
                    }

                    // Wait 200ms before next poll for test start
                    await new Promise(resolve => setTimeout(resolve, 200));
                } catch (pollError) {
                    await new Promise(resolve => setTimeout(resolve, 500));
                    continue;
                }
            }

            if (!testStarted) {
                throw new Error('Test execution failed to start - no new test run ID detected');
            }

            // Now wait for completion with the specific test run ID
            while (Date.now() - startTime < timeoutMs) {
                try {
                    const statusResponse = await this.makeHttpRequest('/tests-status');

                    // Check for errors during test execution
                    if (statusResponse.hasError && statusResponse.errorMessage) {
                        throw new Error(`Test execution error: ${statusResponse.errorMessage}`);
                    }

                    // Check if this is the same test run and it's completed
                    if (statusResponse.testRunId === currentTestRunId && statusResponse.status === 'idle') {
                        // Test execution completed for our specific test run
                        const resultText = this.formatTestResults(statusResponse);
                        const formattedText = this.responseFormatter.formatResponse(resultText);

                        return {
                            jsonrpc: '2.0',
                            id,
                            result: {
                                content: [{
                                    type: 'text',
                                    text: formattedText
                                }]
                            }
                        };
                    }

                    // Wait 1 second before next poll
                    await new Promise(resolve => setTimeout(resolve, 1000));
                } catch (pollError) {
                    // Continue polling despite individual request failures
                    await new Promise(resolve => setTimeout(resolve, 2000));
                    continue;
                }
            }

            throw new Error(`Test execution timed out after ${timeoutSeconds} seconds`);

        } catch (error) {
            throw new Error(`Failed to run tests: ${error.message}`);
        }
    }

    async callTestsRunRegex(id, testFilterRegex, testMode, timeoutSeconds) {
        try {
            // Ensure response formatter is ready
            await this.ensureResponseFormatter();

            // Get initial status to capture current test run ID (if any)
            const initialStatus = await this.makeHttpRequest('/tests-status');
            const initialTestRunId = initialStatus.testRunId;

            // Start regex-filtered test execution
            const runResponse = await this.makeHttpRequest(`/tests-run-regex?filter_regex=${encodeURIComponent(testFilterRegex)}&mode=${testMode}`);

            // Wait for test execution to actually start and get new test run ID
            const startTime = Date.now();
            const timeoutMs = timeoutSeconds * 1000;
            let currentTestRunId = initialTestRunId;

            // First, wait for test execution to start (new test run ID)
            let testStarted = false;
            const startCheckTimeout = 10000; // 10 seconds timeout for test start

            while (Date.now() - startTime < startCheckTimeout) {
                try {
                    const statusResponse = await this.makeHttpRequest('/tests-status');

                    // Check for early error detection
                    if (statusResponse.hasError && statusResponse.errorMessage) {
                        throw new Error(`Test execution failed to start: ${statusResponse.errorMessage}`);
                    }

                    if (statusResponse.testRunId && statusResponse.testRunId !== initialTestRunId) {
                        currentTestRunId = statusResponse.testRunId;
                        testStarted = true;
                        break;
                    }

                    // Wait 200ms before next poll for test start
                    await new Promise(resolve => setTimeout(resolve, 200));
                } catch (pollError) {
                    await new Promise(resolve => setTimeout(resolve, 500));
                    continue;
                }
            }

            if (!testStarted) {
                throw new Error('Test execution failed to start - no new test run ID detected');
            }

            // Now wait for completion with the specific test run ID
            while (Date.now() - startTime < timeoutMs) {
                try {
                    const statusResponse = await this.makeHttpRequest('/tests-status');

                    // Check for errors during test execution
                    if (statusResponse.hasError && statusResponse.errorMessage) {
                        throw new Error(`Test execution error: ${statusResponse.errorMessage}`);
                    }

                    // Check if this is the same test run and it's completed
                    if (statusResponse.testRunId === currentTestRunId && statusResponse.status === 'idle') {
                        // Test execution completed for our specific test run
                        const resultText = this.formatTestResults(statusResponse);
                        const formattedText = this.responseFormatter.formatResponse(resultText);

                        return {
                            jsonrpc: '2.0',
                            id,
                            result: {
                                content: [{
                                    type: 'text',
                                    text: formattedText
                                }]
                            }
                        };
                    }

                    // Wait 1 second before next poll
                    await new Promise(resolve => setTimeout(resolve, 1000));
                } catch (pollError) {
                    // Continue polling despite individual request failures
                    await new Promise(resolve => setTimeout(resolve, 2000));
                    continue;
                }
            }

            throw new Error(`Test execution timed out after ${timeoutSeconds} seconds`);

        } catch (error) {
            throw new Error(`Failed to run tests: ${error.message}`);
        }
    }

    async callRefreshAssets(id, force = false) {
        try {
            // Ensure response formatter is ready
            await this.ensureResponseFormatter();

            // Call Unity refresh endpoint with force parameter
            const refreshResponse = await this.makeHttpRequest(`/refresh-assets?force=${force}`);

            const responseText = refreshResponse.message || 'Asset database refresh completed.';
            const formattedText = this.responseFormatter.formatResponse(responseText);

            return {
                jsonrpc: '2.0',
                id,
                result: {
                    content: [{
                        type: 'text',
                        text: formattedText
                    }]
                }
            };

        } catch (error) {
            throw new Error(`Failed to refresh assets: ${error.message}`);
        }
    }

    async callEditorStatus(id) {
        try {
            // Ensure response formatter is ready
            await this.ensureResponseFormatter();

            // Call Unity editor-status endpoint
            const statusResponse = await this.makeHttpRequest('/editor-status');

            const formattedText = this.responseFormatter.formatJsonResponse(statusResponse);

            return {
                jsonrpc: '2.0',
                id,
                result: {
                    content: [{
                        type: 'text',
                        text: formattedText
                    }]
                }
            };

        } catch (error) {
            throw new Error(`Failed to get editor status: ${error.message}`);
        }
    }

    async callCompileStatus(id) {
        try {
            // Ensure response formatter is ready
            await this.ensureResponseFormatter();

            // Call Unity compilation-status endpoint
            const statusResponse = await this.makeHttpRequest('/compilation-status');

            const formattedText = this.responseFormatter.formatJsonResponse(statusResponse);

            return {
                jsonrpc: '2.0',
                id,
                result: {
                    content: [{
                        type: 'text',
                        text: formattedText
                    }]
                }
            };

        } catch (error) {
            throw new Error(`Failed to get compile status: ${error.message}`);
        }
    }

    async callTestStatus(id) {
        try {
            // Ensure response formatter is ready
            await this.ensureResponseFormatter();

            // Call Unity tests-status endpoint
            const statusResponse = await this.makeHttpRequest('/tests-status');

            const formattedText = this.responseFormatter.formatJsonResponse(statusResponse);

            return {
                jsonrpc: '2.0',
                id,
                result: {
                    content: [{
                        type: 'text',
                        text: formattedText
                    }]
                }
            };

        } catch (error) {
            throw new Error(`Failed to get test status: ${error.message}`);
        }
    }

    async callTestsCancel(id, testRunGuid = '') {
        try {
            // Ensure response formatter is ready
            await this.ensureResponseFormatter();

            // Build the URL with optional guid parameter
            let url = '/tests-cancel';
            if (testRunGuid) {
                url += `?guid=${encodeURIComponent(testRunGuid)}`;
            }

            // Call Unity tests-cancel endpoint
            const cancelResponse = await this.makeHttpRequest(url);

            const formattedText = this.responseFormatter.formatJsonResponse(cancelResponse);

            return {
                jsonrpc: '2.0',
                id,
                result: {
                    content: [{
                        type: 'text',
                        text: formattedText
                    }]
                }
            };

        } catch (error) {
            throw new Error(`Failed to cancel tests: ${error.message}`);
        }
    }

    async callCompileShader(id, shaderName, timeoutSeconds) {
        try {
            await this.ensureResponseFormatter();

            const timeoutMs = timeoutSeconds * 1000;
            const requestBody = JSON.stringify({ shaderName: shaderName });
            const compileResponse = await this.makeHttpPostRequest('/compile-shader', requestBody, timeoutMs);

            const formattedText = this.formatShaderCompileResponse(compileResponse);
            const finalText = this.responseFormatter.formatResponse(formattedText);

            return {
                jsonrpc: '2.0', id,
                result: { content: [{ type: 'text', text: finalText }] }
            };
        } catch (error) {
            throw new Error(`Failed to compile shader: ${error.message}`);
        }
    }

    async callCompileAllShaders(id, timeoutSeconds) {
        try {
            await this.ensureResponseFormatter();

            const timeoutMs = timeoutSeconds * 1000;
            const compileResponse = await this.makeHttpPostRequest('/compile-all-shaders', '{}', timeoutMs);

            const formattedText = this.formatCompileAllShadersResponse(compileResponse);
            const finalText = this.responseFormatter.formatResponse(formattedText);

            return {
                jsonrpc: '2.0', id,
                result: { content: [{ type: 'text', text: finalText }] }
            };
        } catch (error) {
            throw new Error(`Failed to compile all shaders: ${error.message}`);
        }
    }

    async callCompileShadersRegex(id, pattern, timeoutSeconds, progressToken) {
        if (progressToken) {
            // Asynchronous mode with progress notifications
            return await this.callCompileShadersRegexWithProgress(id, pattern, timeoutSeconds, progressToken);
        } else {
            // Original blocking mode (backward compatibility)
            return await this.callCompileShadersRegexBlocking(id, pattern, timeoutSeconds);
        }
    }

    async callCompileShadersRegexBlocking(id, pattern, timeoutSeconds) {
        try {
            await this.ensureResponseFormatter();

            const timeoutMs = timeoutSeconds * 1000;
            const requestBody = JSON.stringify({ pattern });
            const response = await this.makeHttpPostRequest('/compile-shaders-regex', requestBody, timeoutMs);

            const formattedText = this.formatCompileShadersRegexResponse(response);
            const finalText = this.responseFormatter.formatResponse(formattedText);

            return {
                jsonrpc: '2.0', id,
                result: { content: [{ type: 'text', text: finalText }] }
            };
        } catch (error) {
            throw new Error(`Failed to compile shaders by regex: ${error.message}`);
        }
    }

    async callCompileShadersRegexWithProgress(id, pattern, timeoutSeconds, progressToken) {
        try {
            await this.ensureResponseFormatter();

            const timeoutMs = timeoutSeconds * 1000;
            const startTime = Date.now();

            // 1. Start compilation asynchronously
            const startBody = JSON.stringify({ pattern, async: true });
            await this.makeHttpPostRequest('/compile-shaders-regex', startBody);

            // 2. Poll for progress
            let lastProgress = -1;
            while (Date.now() - startTime < timeoutMs) {
                try {
                    const statusResponse = await this.makeHttpRequest('/shader-compilation-status');
                    const status = JSON.parse(statusResponse);

                    // Check if compilation complete
                    if (!status.isCompiling && status.lastCompilationType === 'regex') {
                        // Return final result
                        const formatted = this.formatCompileShadersRegexResponse(status.lastCompilationResult);
                        const finalText = this.responseFormatter.formatResponse(formatted);
                        return {
                            jsonrpc: '2.0',
                            id,
                            result: { content: [{ type: 'text', text: finalText }] }
                        };
                    }

                    // Send progress notification if progress changed
                    if (status.isCompiling && status.progress) {
                        const currentProgress = status.progress.completedShaders;
                        if (currentProgress > lastProgress) {
                            const currentShaderName = status.progress.currentShader ?
                                status.progress.currentShader.split('/').pop() : '';
                            this.sendProgressNotification(
                                progressToken,
                                currentProgress,
                                status.progress.totalShaders,
                                `Compiling ${currentShaderName} (${currentProgress}/${status.progress.totalShaders})`
                            );
                            lastProgress = currentProgress;
                        }
                    }

                    // Wait before next poll
                    await new Promise(resolve => setTimeout(resolve, 500)); // 500ms polling interval

                } catch (pollError) {
                    // Handle Unity restart errors
                    if (this.isUnityRestartingError(pollError)) {
                        await new Promise(resolve => setTimeout(resolve, 2000));
                        continue;
                    }
                    throw pollError;
                }
            }

            // Timeout
            throw new Error(`Shader compilation timed out after ${timeoutSeconds} seconds`);
        } catch (error) {
            throw new Error(`Failed to compile shaders by regex with progress: ${error.message}`);
        }
    }

    async callShaderCompilationStatus(id) {
        try {
            await this.ensureResponseFormatter();

            const statusResponse = await this.makeHttpRequest('/shader-compilation-status');

            const formattedText = this.responseFormatter.formatJsonResponse(statusResponse);

            return {
                jsonrpc: '2.0', id,
                result: { content: [{ type: 'text', text: formattedText }] }
            };
        } catch (error) {
            throw new Error(`Failed to get shader compilation status: ${error.message}`);
        }
    }

    async callEditorLogPath(id) {
        try {
            await this.ensureResponseFormatter();

            const logPath = this.getUnityEditorLogPath();
            const exists = await this.fileExists(logPath);

            const response = {
                path: logPath,
                exists: exists,
                platform: process.platform
            };

            const formattedText = this.responseFormatter.formatJsonResponse(response);

            return {
                jsonrpc: '2.0', id,
                result: { content: [{ type: 'text', text: formattedText }] }
            };
        } catch (error) {
            throw new Error(`Failed to get editor log path: ${error.message}`);
        }
    }

    async callEditorLogHead(id, lineCount, logType) {
        try {
            await this.ensureResponseFormatter();

            const logPath = this.getUnityEditorLogPath();

            if (!await this.fileExists(logPath)) {
                throw new Error(`Unity Editor log file not found at: ${logPath}. Ensure Unity Editor is running.`);
            }

            const lines = await this.readLogHead(logPath, lineCount);
            const filtered = this.filterLogLines(lines, logType);
            const formattedText = this.formatLogOutput(filtered, 'head', lineCount, logType, logPath);
            const finalText = this.responseFormatter.formatResponse(formattedText);

            return {
                jsonrpc: '2.0', id,
                result: { content: [{ type: 'text', text: finalText }] }
            };
        } catch (error) {
            throw new Error(`Failed to read log head: ${error.message}`);
        }
    }

    async callEditorLogTail(id, lineCount, logType) {
        try {
            await this.ensureResponseFormatter();

            const logPath = this.getUnityEditorLogPath();

            if (!await this.fileExists(logPath)) {
                throw new Error(`Unity Editor log file not found at: ${logPath}. Ensure Unity Editor is running.`);
            }

            const lines = await this.readLogTail(logPath, lineCount);
            const filtered = this.filterLogLines(lines, logType);
            const formattedText = this.formatLogOutput(filtered, 'tail', lineCount, logType, logPath);
            const finalText = this.responseFormatter.formatResponse(formattedText);

            return {
                jsonrpc: '2.0', id,
                result: { content: [{ type: 'text', text: finalText }] }
            };
        } catch (error) {
            throw new Error(`Failed to read log tail: ${error.message}`);
        }
    }

    async callEditorLogGrep(id, pattern, caseSensitive, contextLines, lineLimit, logType) {
        try {
            await this.ensureResponseFormatter();

            const logPath = this.getUnityEditorLogPath();

            if (!await this.fileExists(logPath)) {
                throw new Error(`Unity Editor log file not found at: ${logPath}. Ensure Unity Editor is running.`);
            }

            const matches = await this.grepLog(logPath, pattern, caseSensitive, contextLines, lineLimit);
            const filtered = this.filterLogLines(matches.map(m => m.line), logType);

            // Reconstruct matches array with only filtered lines
            const filteredMatches = matches.filter(m => filtered.includes(m.line));

            const formattedText = this.formatGrepOutput(filteredMatches, pattern, caseSensitive, contextLines, logType, logPath);
            const finalText = this.responseFormatter.formatResponse(formattedText);

            return {
                jsonrpc: '2.0', id,
                result: { content: [{ type: 'text', text: finalText }] }
            };
        } catch (error) {
            throw new Error(`Failed to grep log: ${error.message}`);
        }
    }

    async callExecuteMenuItem(id, menuItemPath) {
        try {
            await this.ensureResponseFormatter();

            const response = await this.makeHttpRequest(`/execute-menu-item?menuItemPath=${encodeURIComponent(menuItemPath)}`);
            const formattedText = this.responseFormatter.formatJsonResponse(response);

            return {
                jsonrpc: '2.0', id,
                result: { content: [{ type: 'text', text: formattedText }] }
            };
        } catch (error) {
            throw new Error(`Failed to execute menu item: ${error.message}`);
        }
    }

    formatShaderCompileResponse(response) {
        let text = '';

        if (response.status === 'error') {
            return `Error: ${response.message}`;
        }

        if (response.allMatches && response.allMatches.length > 1) {
            text += `Found ${response.allMatches.length} matching shaders:\n`;
            response.allMatches.forEach((match, idx) => {
                const prefix = idx === 0 ? '✓ AUTO-SELECTED' : '  ';
                text += `${prefix} [Score: ${match.matchScore}] ${match.name}\n`;
                text += `    Path: ${match.path}\n`;
            });
            text += '\n';
        }

        if (response.result) {
            const result = response.result;
            text += `Shader: ${result.shaderName}\n`;
            text += `Path: ${result.shaderPath}\n`;
            text += `Compilation Time: ${result.compilationTime.toFixed(2)}s\n`;
            text += `Status: ${result.hasErrors ? '❌ FAILED' : '✓ SUCCESS'}\n`;

            if (result.targetPlatforms && result.targetPlatforms.length > 0) {
                text += `Target Platforms: ${result.targetPlatforms.join(', ')}\n`;
            }

            if (result.hasErrors || result.hasWarnings) {
                text += `Errors: ${result.errorCount}, Warnings: ${result.warningCount}\n\n`;

                if (result.errors && result.errors.length > 0) {
                    text += 'ERRORS:\n';
                    result.errors.forEach(err => {
                        text += `  ${err.file}:${err.line} - ${err.message}\n`;
                        if (err.messageDetails) {
                            text += `    Details: ${err.messageDetails}\n`;
                        }
                    });
                }

                if (result.warnings && result.warnings.length > 0) {
                    text += '\nWARNINGS:\n';
                    result.warnings.forEach(warn => {
                        text += `  ${warn.file}:${warn.line} - ${warn.message}\n`;
                    });
                }
            }
        }

        return text;
    }

    formatCompileAllShadersResponse(response) {
        let text = `Shader Compilation Summary:\n`;
        text += `Total Shaders: ${response.totalShaders}\n`;
        text += `Successful: ${response.successfulCompilations}\n`;
        text += `Failed: ${response.failedCompilations}\n`;
        text += `Total Time: ${response.totalCompilationTime.toFixed(2)}s\n\n`;

        if (response.results && response.failedCompilations > 0) {
            text += 'FAILED SHADERS:\n';
            response.results.filter(r => r.hasErrors).forEach(result => {
                text += `\n❌ ${result.shaderName}\n`;
                text += `   Path: ${result.shaderPath}\n`;
                text += `   Errors: ${result.errorCount}\n`;

                if (result.errors && result.errors.length > 0) {
                    result.errors.slice(0, 3).forEach(err => {
                        text += `   - ${err.file}:${err.line}: ${err.message}\n`;
                    });
                    if (result.errors.length > 3) {
                        text += `   ... and ${result.errors.length - 3} more errors\n`;
                    }
                }
            });
        }

        return text;
    }

    formatCompileShadersRegexResponse(response) {
        if (response.status === 'error') {
            return `❌ Shader Regex Compilation Failed\n\n${response.message}`;
        }

        let text = '=== Shader Regex Compilation Results ===\n\n';
        text += `Pattern: ${response.pattern}\n`;
        text += `Total Matched: ${response.totalShaders}\n`;
        text += `Successful: ${response.successfulCompilations}\n`;
        text += `Failed: ${response.failedCompilations}\n`;
        text += `Total Time: ${response.totalCompilationTime.toFixed(2)}s\n\n`;

        if (response.failedCompilations > 0 && response.results) {
            text += 'Failed Shaders:\n';
            response.results.filter(r => r.hasErrors).forEach(shader => {
                text += `\n❌ ${shader.shaderName}\n`;
                text += `   Path: ${shader.shaderPath}\n`;
                text += `   Errors: ${shader.errorCount}\n`;

                if (shader.errors && shader.errors.length > 0) {
                    const errorLimit = 3;
                    shader.errors.slice(0, errorLimit).forEach(err => {
                        const fileInfo = err.file ? ` (${err.file}:${err.line})` : '';
                        text += `   • ${err.message}${fileInfo}\n`;
                    });

                    if (shader.errors.length > errorLimit) {
                        text += `   ... and ${shader.errors.length - errorLimit} more errors\n`;
                    }
                }
            });
        }

        return text;
    }

    formatTestResults(statusResponse) {
        if (!statusResponse.testResults) {
            return 'Test execution completed but no results available.';
        }

        const { totalTests, passedTests, failedTests, skippedTests, duration, results } = statusResponse.testResults;

        let resultText = `Test Results:\n`;
        resultText += `Total: ${totalTests}, Passed: ${passedTests}, Failed: ${failedTests}, Skipped: ${skippedTests}\n`;
        resultText += `Duration: ${duration}s\n\n`;

        if (failedTests > 0 && results) {
            resultText += 'Failed Tests:\n';
            results.filter(test => test.outcome === 'Failed').forEach(test => {
                resultText += `- ${test.name}: ${test.message}\n`;
            });
        }

        return resultText;
    }

    getUnityEditorLogPath() {
        const platform = process.platform;
        const home = require('os').homedir();

        switch (platform) {
            case 'win32':
                return path.join(process.env.LOCALAPPDATA || path.join(home, 'AppData', 'Local'),
                               'Unity', 'Editor', 'Editor.log');
            case 'darwin':
                return path.join(home, 'Library', 'Logs', 'Unity', 'Editor.log');
            case 'linux':
                return path.join(home, '.config', 'unity3d', 'Editor.log');
            default:
                throw new Error(`Unsupported platform: ${platform}`);
        }
    }

    async fileExists(filePath) {
        try {
            await fs.promises.access(filePath);
            return true;
        } catch {
            return false;
        }
    }

    async readLogHead(logPath, lineCount) {
        const stream = require('fs').createReadStream(logPath, { encoding: 'utf8' });
        const readline = require('readline');
        const rl = readline.createInterface({ input: stream });

        const lines = [];
        for await (const line of rl) {
            lines.push(line);
            if (lines.length >= lineCount) {
                rl.close();
                stream.destroy();
                break;
            }
        }
        return lines;
    }

    async readLogTail(logPath, lineCount) {
        const content = await fs.promises.readFile(logPath, 'utf8');
        const allLines = content.split('\n');
        return allLines.slice(-lineCount);
    }

    async grepLog(logPath, pattern, caseSensitive, contextLines, lineLimit) {
        try {
            const flags = caseSensitive ? 'g' : 'gi';
            const regex = new RegExp(pattern, flags);

            const content = await fs.promises.readFile(logPath, 'utf8');
            const allLines = content.split('\n');
            const matches = [];
            const addedLines = new Set();

            for (let i = 0; i < allLines.length && matches.length < lineLimit; i++) {
                if (regex.test(allLines[i])) {
                    const start = Math.max(0, i - contextLines);
                    const end = Math.min(allLines.length, i + contextLines + 1);

                    for (let j = start; j < end; j++) {
                        const lineKey = `${j}:${allLines[j]}`;
                        if (!addedLines.has(lineKey)) {
                            matches.push({
                                line: allLines[j],
                                lineNumber: j + 1,
                                isMatch: j === i,
                                context: j !== i
                            });
                            addedLines.add(lineKey);
                        }
                    }
                }
            }

            return matches;
        } catch (error) {
            if (error.message.includes('Invalid regular expression')) {
                throw new Error(`Invalid regex pattern: ${pattern}. ${error.message}`);
            }
            throw error;
        }
    }

    detectLogType(line) {
        const lowerLine = line.toLowerCase();

        // Stack trace lines inherit type from parent
        if (/^\s+at\s+/.test(line) || /^\s+\w+\.\w+:/.test(line)) {
            return 'stack_trace';
        }

        // Error: contains "error", "exception:", "assertion", "failed", or "shader error"
        if (lowerLine.includes('error') || lowerLine.includes('exception:') ||
            lowerLine.includes('assertion') || lowerLine.includes('failed') ||
            /shader error/i.test(line)) {
            return 'error';
        }

        // Warning: contains "warning:" or "shader warning"
        if (lowerLine.includes('warning:') || /^\[.*\]\s+.*warning/i.test(line) ||
            /shader warning/i.test(line)) {
            return 'warning';
        }

        // Everything else is info
        return 'info';
    }

    filterLogLines(lines, logType) {
        if (logType === 'all') {
            return lines;
        }

        const filtered = [];
        let currentType = 'info';

        for (const line of lines) {
            const lineType = this.detectLogType(line);

            if (lineType === 'stack_trace') {
                // Inherit type from previous line
                if (currentType === logType) {
                    filtered.push(line);
                }
            } else {
                currentType = lineType;
                if (lineType === logType) {
                    filtered.push(line);
                }
            }
        }

        return filtered;
    }

    formatLogOutput(lines, operation, lineCount, logType, logPath) {
        let output = `=== Unity Editor Log (${operation}) ===\n`;
        output += `Path: ${logPath}\n`;
        output += `Lines: ${lines.length}`;
        if (logType !== 'all') {
            output += ` (filtered: ${logType})`;
        }
        output += '\n\n';

        output += lines.join('\n');
        return output;
    }

    formatGrepOutput(matches, pattern, caseSensitive, contextLines, logType, logPath) {
        let output = `=== Unity Editor Log Grep Results ===\n`;
        output += `Path: ${logPath}\n`;
        output += `Pattern: ${pattern} (case ${caseSensitive ? 'sensitive' : 'insensitive'})\n`;
        output += `Matches: ${matches.filter(m => m.isMatch).length}`;
        if (logType !== 'all') {
            output += ` (filtered: ${logType})`;
        }
        output += '\n\n';

        for (const match of matches) {
            const prefix = match.isMatch ? '> ' : '  ';
            const lineNum = String(match.lineNumber).padStart(6, ' ');
            output += `${prefix}${lineNum}: ${match.line}\n`;
        }

        return output;
    }

    async makeHttpRequest(path) {
        return new Promise((resolve, reject) => {
            const req = http.request(`${this.unityServerUrl}${path}`, { method: 'GET' }, (res) => {
                let data = '';
                res.on('data', chunk => data += chunk);
                res.on('end', () => {
                    try {
                        const parsed = JSON.parse(data);
                        resolve(parsed);
                    } catch (error) {
                        reject(new Error(`Invalid JSON response: ${data}`));
                    }
                });
            });

            req.on('error', (error) => {
                // Enhanced error detection for Unity server issues
                reject(this.createUnityServerError(error));
            });

            req.setTimeout(15000, () => {
                req.destroy();
                reject(this.createUnityTimeoutError());
            });

            req.end();
        });
    }

    makeHttpPostRequest(path, body, timeoutMs = 15000) {
        return new Promise((resolve, reject) => {
            const options = {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Content-Length': Buffer.byteLength(body)
                }
            };

            const req = http.request(`${this.unityServerUrl}${path}`, options, (res) => {
                let data = '';
                res.on('data', chunk => data += chunk);
                res.on('end', () => {
                    try {
                        const parsed = JSON.parse(data);
                        resolve(parsed);
                    } catch (error) {
                        reject(new Error(`Invalid JSON response: ${data}`));
                    }
                });
            });

            req.on('error', (error) => {
                reject(this.createUnityServerError(error));
            });

            req.setTimeout(timeoutMs, () => {
                req.destroy();
                reject(this.createUnityTimeoutError());
            });

            req.write(body);
            req.end();
        });
    }

    createUnityServerError(error) {
        // Detect Unity server unavailability vs temporary restart
        if (error.code === 'ECONNREFUSED') {
            // Unity Editor not running or HTTP server not started
            return new UnityUnavailableError(
                `Unity Editor HTTP server unavailable at ${this.unityServerUrl}`,
                {
                    errorType: 'unity_server_unavailable',
                    instructions: 'INSTRUCTIONS FOR LLM: 1) Verify Unity Editor is running 2) Check if Unity HTTP server is active (should start automatically) 3) Test with: curl http://localhost:17932/compilation-status 4) If Unity is running but server is down, advise user to restart Unity Editor',
                    retryable: false,
                    originalError: error.message
                }
            );
        } else if (error.code === 'ECONNRESET' || error.code === 'EPIPE') {
            // Unity HTTP server restarting (temporary)
            return new UnityRestartingError(
                'Unity HTTP server restarting during compilation/asset refresh',
                {
                    errorType: 'unity_server_restarting',
                    instructions: 'INSTRUCTIONS FOR LLM: This is normal behavior during Unity compilation. Wait 3-5 seconds and retry the operation. Unity automatically restarts HTTP server during script compilation and asset database refresh.',
                    retryable: true,
                    originalError: error.message
                }
            );
        } else {
            // Other HTTP errors
            return new Error(`HTTP request failed: ${error.message}`);
        }
    }

    createUnityTimeoutError() {
        return new UnityRestartingError(
            'Unity HTTP server timeout - likely restarting during compilation',
            {
                errorType: 'unity_server_restarting',
                instructions: 'INSTRUCTIONS FOR LLM: Unity server timeout usually indicates compilation or asset refresh in progress. Wait 3-5 seconds and retry the operation.',
                retryable: true,
                originalError: 'Request timeout after 15 seconds'
            }
        );
    }

    async checkUnityServerHealth() {
        try {
            const response = await this.makeHttpRequest('/compilation-status');
            return { available: true, response };
        } catch (error) {
            return { available: false, error };
        }
    }

    start() {
        this.stdinBuffer = Buffer.alloc(0);

        process.stdin.on('data', (chunk) => {
            const bufferChunk = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk, 'utf8');
            this.stdinBuffer = Buffer.concat([this.stdinBuffer, bufferChunk]);
            this.processInputBuffer();
        });

        process.stdin.on('end', () => {
            process.exit(0);
        });
    }

    processInputBuffer() {
        while (true) {
            this.stripIgnorablePrefix();

            if (this.stdinBuffer.length === 0) {
                return;
            }

            if (this.activeProtocol === 'content-length') {
                const framedMessage = this.tryConsumeFramedMessage();
                if (framedMessage === null) {
                    return;
                }
                this.handleIncomingMessage(framedMessage, 'content-length');
                continue;
            }

            if (this.activeProtocol === 'newline') {
                const lineMessage = this.tryConsumeLineMessage();
                if (lineMessage === null) {
                    return;
                }
                if (!lineMessage) {
                    continue;
                }
                this.handleIncomingMessage(lineMessage, 'newline');
                continue;
            }

            const firstByte = this.stdinBuffer[0];
            if (firstByte === 0x7b || firstByte === 0x5b) { // '{' or '['
                const jsonLine = this.tryConsumeLineMessage();
                if (jsonLine === null) {
                    return;
                }
                if (!jsonLine) {
                    continue;
                }
                this.activeProtocol = 'newline';
                this.handleIncomingMessage(jsonLine, 'newline');
                continue;
            }

            const framed = this.tryConsumeFramedMessage();
            if (framed === null) {
                return;
            }
            this.activeProtocol = 'content-length';
            this.handleIncomingMessage(framed, 'content-length');
        }
    }

    stripIgnorablePrefix() {
        // Remove UTF-8 BOM if present
        if (this.stdinBuffer.length >= 3 &&
            this.stdinBuffer[0] === 0xEF &&
            this.stdinBuffer[1] === 0xBB &&
            this.stdinBuffer[2] === 0xBF) {
            this.stdinBuffer = this.stdinBuffer.slice(3);
        }

        let dropCount = 0;
        while (dropCount < this.stdinBuffer.length) {
            const byte = this.stdinBuffer[dropCount];
            if (byte === 0x0d || byte === 0x0a || byte === 0x09 || byte === 0x20) {
                dropCount++;
                continue;
            }
            break;
        }

        if (dropCount > 0) {
            this.stdinBuffer = this.stdinBuffer.slice(dropCount);
        }
    }

    tryConsumeLineMessage() {
        const newlineIndex = this.stdinBuffer.indexOf(0x0a);
        if (newlineIndex === -1) {
            return null;
        }

        const lineBuffer = this.stdinBuffer.slice(0, newlineIndex);
        this.stdinBuffer = this.stdinBuffer.slice(newlineIndex + 1);

        let line = lineBuffer.toString('utf8');
        if (line.endsWith('\r')) {
            line = line.slice(0, -1);
        }

        return line.trim();
    }

    tryConsumeFramedMessage() {
        let headerEnd = this.stdinBuffer.indexOf('\r\n\r\n');
        let delimiterLength = 4;

        if (headerEnd === -1) {
            headerEnd = this.stdinBuffer.indexOf('\n\n');
            delimiterLength = 2;
        }

        if (headerEnd === -1) {
            return null;
        }

        const headerText = this.stdinBuffer.slice(0, headerEnd).toString('utf8');
        const headerLines = headerText.split(/\r?\n/);
        let contentLength = null;

        for (const line of headerLines) {
            const separatorIndex = line.indexOf(':');
            if (separatorIndex === -1) {
                continue;
            }

            const key = line.slice(0, separatorIndex).trim().toLowerCase();
            if (key === 'content-length') {
                const rawValue = line.slice(separatorIndex + 1).trim();
                const parsed = parseInt(rawValue, 10);
                if (!Number.isNaN(parsed) && parsed >= 0) {
                    contentLength = parsed;
                    break;
                }
            }
        }

        if (contentLength === null) {
            return null;
        }

        const totalLength = headerEnd + delimiterLength + contentLength;
        if (this.stdinBuffer.length < totalLength) {
            return null;
        }

        const bodyBuffer = this.stdinBuffer.slice(headerEnd + delimiterLength, totalLength);
        this.stdinBuffer = this.stdinBuffer.slice(totalLength);
        return bodyBuffer.toString('utf8');
    }

    handleIncomingMessage(rawMessage, protocol) {
        let request;
        try {
            request = JSON.parse(rawMessage);
        } catch (_) {
            logger?.logError('Failed to parse incoming message', new Error('Invalid JSON'));
            this.sendParseError(protocol);
            return;
        }

        // Log incoming request (includes _meta if present)
        logger?.logRequest(request.method, request.params, request.id);

        Promise.resolve(this.handleRequest(request))
            .then((response) => {
                if (response) {
                    logger?.logResponse(response);
                    this.sendJsonResponse(response, protocol);
                }
            })
            .catch((error) => {
                const id = (request && typeof request === 'object') ? (request.id ?? null) : null;
                logger?.logError(`Request handler failed for method: ${request?.method}`, error);
                this.sendRpcError(id, error, protocol);
            });
    }

    sendJsonResponse(payload, protocol = this.activeProtocol || 'newline') {
        const json = JSON.stringify(payload);

        if (protocol === 'content-length') {
            const byteLength = Buffer.byteLength(json, 'utf8');
            this.writeStdoutData(`Content-Length: ${byteLength}\r\n\r\n`);
            this.writeStdoutData(json);
        } else {
            this.writeStdoutData(`${json}\n`);
        }
    }

    writeStdoutData(data) {
        const writer = typeof global.safeStdoutWrite === 'function'
            ? global.safeStdoutWrite
            : (chunk) => process.stdout.write(chunk);
        writer(data);
    }

    sendParseError(protocol) {
        const response = {
            jsonrpc: '2.0',
            id: null,
            error: {
                code: -32700,
                message: 'Parse error'
            }
        };
        this.sendJsonResponse(response, protocol);
    }

    sendRpcError(id, error, protocol) {
        const response = {
            jsonrpc: '2.0',
            id,
            error: this.formatErrorObject(error)
        };
        this.sendJsonResponse(response, protocol);
    }

    formatErrorObject(error) {
        if (error instanceof UnityUnavailableError) {
            return {
                code: -32001,
                message: error.message,
                data: error.data
            };
        }

        if (error instanceof UnityRestartingError) {
            return {
                code: -32002,
                message: error.message,
                data: error.data
            };
        }

        const errorPayload = {
            code: -32603,
            message: (error && error.message) ? error.message : 'Internal error'
        };

        if (error && error.data) {
            errorPayload.data = error.data;
        }

        return errorPayload;
    }
}

// Parse command-line arguments for --port and --log-file
let port = 17932;
let logFilePath = null;

for (let i = 2; i < process.argv.length; i++) {
  if (process.argv[i] === "--port") {
    i++;
    if (i < process.argv.length) {
      const parsedPort = parseInt(process.argv[i], 10);
      if (!isNaN(parsedPort) && parsedPort >= 1024 && parsedPort <= 65535) {
        port = parsedPort;
      } else {
        console.error(`Invalid port value: ${process.argv[i]}. Using default 17932.`);
      }
    } else {
      console.error("--port requires a value. Using default 17932.");
    }
  } else if (process.argv[i] === "--log-file") {
    i++;
    if (i < process.argv.length) {
      logFilePath = process.argv[i];
    } else {
      console.error("--log-file requires a file path.");
    }
  }
}

// Initialize logger (disabled if logFilePath is null)
logger = new FileLogger(logFilePath);
logger.logInfo('MCP Server starting', { port, logFilePath });

const server = new MCPServer(port);
server.start();
