using System;
using System.Collections.Generic;
using Nyamu.Core.Interfaces;

namespace Nyamu.Core
{
    // Wrapper for Unity main thread action queue
    // Unity APIs must be called from the main thread
    public class UnityThreadExecutor : IUnityThreadExecutor
    {
        readonly Queue<Action> _actionQueue = new();

        public void Enqueue(Action action)
        {
            lock (_actionQueue)
            {
                _actionQueue.Enqueue(action);
            }
        }

        public void Process()
        {
            lock (_actionQueue)
            {
                while (_actionQueue.Count > 0)
                    _actionQueue.Dequeue().Invoke();
            }
        }
    }
}
