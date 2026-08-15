using System;

namespace Nyamu.Tools.Editor
{
    // Response DTO for executing menu item
    [Serializable]
    public class ExecuteMenuItemResponse
    {
        public bool success;
        public string status;   // ok | not_executed | main_thread_timeout | error
        public string message;
        public string menuItemPath;
    }
}
