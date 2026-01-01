using System;

namespace Nyamu.Tools.Settings
{
    [Serializable]
    public class McpSettingsResponse
    {
        public int responseCharacterLimit;
        public bool enableTruncation;
        public string truncationMessage;
    }
}
