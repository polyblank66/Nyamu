using System;

namespace Nyamu.Tools.Editor
{
    // Request DTO for executing menu item
    [Serializable]
    public class ExecuteMenuItemRequest
    {
        public string menuItemPath;
    }
}
