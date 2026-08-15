using System;
using System.Collections.Generic;
using Nyamu.Core.Interfaces;

namespace Nyamu.Core
{
    // Wrapper for Unity main thread action queue
    // Unity APIs must be called from the main thread
    public class UnityThreadExecutor : IUnityThreadExecutor
    {
        private readonly Queue<Action> _actionQueue = new();
        private readonly List<Action> _batch = new(); // main thread only: Process() runs from EditorMonitor.OnEditorUpdate

        public void Enqueue(Action action)
        {
            if (action == null)
                return;

            lock (_actionQueue)
            {
                _actionQueue.Enqueue(action);
            }
        }

        public void Process()
        {
            _batch.Clear();
            lock (_actionQueue)
            {
                while (_actionQueue.Count > 0)
                    _batch.Add(_actionQueue.Dequeue());
            }

            // Invoked outside the lock: a re-entrant Enqueue must not self-deadlock, and a slow
            // action must not block HTTP handler threads (TcpHttpServer.cs) trying to enqueue.
            // Per-action try/catch so one throwing action neither aborts the rest of the batch
            // nor escapes into EditorApplication.update.
            foreach (var action in _batch)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    NyamuLogger.LogError($"[Nyamu][UnityThreadExecutor] Queued action threw: {ex}");
                }
            }
            _batch.Clear();
        }
    }
}
