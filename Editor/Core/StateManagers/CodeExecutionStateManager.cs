using System.Collections.Generic;
using Nyamu.Tools.CodeExecution;

namespace Nyamu.Core.StateManagers
{
    // Tracks in-flight and recently-finished code_execute runs. Only one execution may be in
    // flight at a time (TryBegin enforces this); a small ring of finished records is kept so
    // code_execute_status can still answer after the run completes.
    public class CodeExecutionStateManager
    {
        readonly object _lock = new();
        readonly Dictionary<string, CodeExecutionRecord> _records = new();
        readonly List<string> _order = new();
        string _currentExecutionId = "";
        int _assembliesLoadedThisSession;
        const int MaxRecords = 5;

        internal bool TryBegin(CodeExecutionRecord record)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_currentExecutionId) &&
                    _records.TryGetValue(_currentExecutionId, out var current) &&
                    !current.IsDone)
                {
                    return false;
                }

                _records[record.Id] = record;
                _order.Add(record.Id);
                _currentExecutionId = record.Id;

                while (_order.Count > MaxRecords)
                {
                    var oldest = _order[0];
                    _order.RemoveAt(0);
                    if (oldest != _currentExecutionId)
                        _records.Remove(oldest);
                }

                return true;
            }
        }

        internal CodeExecutionRecord Get(string executionId)
        {
            lock (_lock)
            {
                var id = string.IsNullOrEmpty(executionId) ? _currentExecutionId : executionId;
                if (string.IsNullOrEmpty(id))
                    return null;
                return _records.TryGetValue(id, out var record) ? record : null;
            }
        }

        internal void IncrementAssembliesLoaded()
        {
            lock (_lock) _assembliesLoadedThisSession++;
        }

        public int AssembliesLoadedThisSession
        {
            get { lock (_lock) return _assembliesLoadedThisSession; }
        }
    }
}
