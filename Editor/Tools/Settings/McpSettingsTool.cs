using System.Threading.Tasks;
using Nyamu.Core.Interfaces;

namespace Nyamu.Tools.Settings
{
    // Tool for retrieving MCP server settings
    public class McpSettingsTool : INyamuTool<McpSettingsRequest, McpSettingsResponse>
    {
        public string Name => "mcp_settings";

        const int SettingsCacheDurationSeconds = 5;

        public Task<McpSettingsResponse> ExecuteAsync(
            McpSettingsRequest request,
            IExecutionContext context)
        {
            var state = context.SettingsState;

            McpSettingsResponse cachedSettings;
            System.DateTime lastRefresh;

            lock (state.Lock)
            {
                cachedSettings = state.CachedSettings;
                lastRefresh = state.LastSettingsRefresh;
            }

            var now = System.DateTime.Now;
            var cacheAge = (now - lastRefresh).TotalSeconds;

            // Return cached settings if fresh enough
            if (cachedSettings != null && cacheAge < SettingsCacheDurationSeconds)
            {
                return Task.FromResult(cachedSettings);
            }

            // Refresh settings from NyamuSettings
            var settings = NyamuSettings.Instance;
            var response = new McpSettingsResponse
            {
                responseCharacterLimit = settings.responseCharacterLimit,
                enableTruncation = settings.enableTruncation,
                truncationMessage = settings.truncationMessage
            };

            // Update cache
            lock (state.Lock)
            {
                state.CachedSettings = response;
                state.LastSettingsRefresh = now;
            }

            return Task.FromResult(response);
        }
    }
}
