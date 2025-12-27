using System;

namespace Nyamu.Core.Interfaces
{
    // Abstraction for executing actions on Unity main thread
    public interface IUnityThreadExecutor
    {
        void Enqueue(Action action);
    }
}
