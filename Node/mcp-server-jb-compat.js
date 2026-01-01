// JetBrains MCP compatibility launcher
// Some JetBrains tools expect a file named `mcp-server-jb-compat.js`.
// This thin wrapper simply delegates to the canonical server implementation,
// and ensures that nothing is written to STDERR (some hosts treat any STDERR
// output as a hard failure). All error logs are redirected to STDOUT.

// 1) Redirect low-level writes to STDERR to STDOUT
const originalStdoutWrite = process.stdout.write.bind(process.stdout);
process.stderr.write = function(...args) {
  try {
    return originalStdoutWrite(...args);
  } catch (e) {
    // As a last resort, swallow to avoid emitting on STDERR
    return true;
  }
};

// 2) Normalize console.error to console.log so logging APIs go to STDOUT
if (console && typeof console.error === 'function') {
  console.error = (...args) => console.log(...args);
}

// 3) Route Node process warnings to STDOUT (instead of STDERR)
process.on('warning', (warning) => {
  try {
    console.log(String(warning && warning.stack ? warning.stack : warning));
  } catch (_) {
    // ignore
  }
});

// 4) Still surface catastrophic issues via STDOUT message before exit.
process.on('uncaughtException', (err) => {
  try { console.log('[mcp-server] uncaughtException:', err && err.stack ? err.stack : String(err)); } catch(_) {}
  // Preserve default non-zero exit to signal failure, but no STDERR writes
  process.exit(1);
});

process.on('unhandledRejection', (reason) => {
  try { console.log('[mcp-server] unhandledRejection:', reason && reason.stack ? reason.stack : String(reason)); } catch(_) {}
  // Non-zero exit to avoid zombie state
  process.exit(1);
});

require('./mcp-server.js');
