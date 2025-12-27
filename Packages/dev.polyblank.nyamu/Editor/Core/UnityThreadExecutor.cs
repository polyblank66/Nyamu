using System;
using System.Collections.Generic;
using Nyamu.Core.Interfaces;

namespace Nyamu.Core
{
    // Wrapper for Unity main thread action queue
    // Unity APIs must be called from the main thread
    public class UnityThreadExecutor : IUnityThreadExecutor
    {
        readonly Queue<Action> _actionQueue;

        public UnityThreadExecutor(Queue<Action> actionQueue)
        {
            _actionQueue = actionQueue;
        }

        public void Enqueue(Action action)
        {
            lock (_actionQueue)
            {
                _actionQueue.Enqueue(action);
            }
        }
    }
}
