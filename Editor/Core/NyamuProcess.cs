using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nyamu
{
    // Identity of the Unity process this domain lives in.
    //
    // Unity runs InitializeOnLoad in more than one process: besides the Editor itself,
    // every AssetImportWorker loads a full editor domain and executes the same code.
    // Only the real Editor may own the MCP port. A worker binds it just as happily -
    // TcpHttpServer sets SO_REUSEADDR, and on Windows that lets several sockets share
    // one port - after which requests are delivered to an arbitrary one of them. A
    // worker either answers with state it does not have (it never enters Play Mode,
    // never runs tests) or, while reloading its own domain, does not answer at all and
    // the caller only sees a timeout.
    internal static class NyamuProcess
    {
        // Safe from any thread: no Unity API involved.
        internal static readonly int Id = Process.GetCurrentProcess().Id;

        // Captured on the main thread by Server.Initialize(). Application.dataPath is
        // main-thread only, while the HTTP handlers that report the path are not.
        internal static string ProjectPath { get; private set; } = string.Empty;

        // Public API since Unity 2021.2; the package targets 2021.3+.
        internal static bool IsAssetImportWorker => AssetDatabase.IsAssetImportWorkerProcess();

        internal static void CaptureProjectPath()
        {
            var root = Directory.GetParent(Application.dataPath);
            ProjectPath = root != null ? Path.GetFullPath(root.FullName) : string.Empty;
        }
    }
}
