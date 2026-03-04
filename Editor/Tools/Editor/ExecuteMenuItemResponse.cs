using System;

namespace Nyamu.Tools.Editor
{
    // Response DTO for executing menu item
    [Serializable]
    public class ExecuteMenuItemResponse
    {
        public string status;
        public string message;
        public string menuItemPath;
    }
}
