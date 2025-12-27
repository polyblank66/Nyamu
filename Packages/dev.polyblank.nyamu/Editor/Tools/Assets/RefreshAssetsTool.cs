using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using UnityEditor;

namespace Nyamu.Tools.Assets
{
    // Tool for refreshing Unity asset database
    public class RefreshAssetsTool : INyamuTool<RefreshAssetsRequest, RefreshAssetsResponse>
    {
        public string Name => "refresh_assets";

        public Task<RefreshAssetsResponse> ExecuteAsync(
            RefreshAssetsRequest request,
            IExecutionContext context)
        {
            var state = context.AssetState;

            // Check if refresh is already in progress
            lock (state.Lock)
            {
                if (state.IsRefreshing)
                {
                    return Task.FromResult(new RefreshAssetsResponse
                    {
                        status = "warning",
                        message = "Asset refresh already in progress. Please wait for current refresh to complete."
                    });
                }

                // Mark refresh as starting
                state.IsRefreshing = true;
            }

            // Queue the refresh operation on main thread
            context.UnityExecutor.Enqueue(() =>
            {
                try
                {
                    if (request.force)
                    {
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    }
                    else
                    {
                        AssetDatabase.Refresh();
                    }

                    // Start monitoring for refresh completion
                    Server.StartRefreshMonitoring(state);
                }
                catch (System.Exception ex)
                {
                    // Reset refresh flags immediately if AssetDatabase.Refresh() fails
                    lock (state.Lock)
                    {
                        state.IsRefreshing = false;
                        state.IsMonitoringRefresh = false;
                        state.UnityIsUpdating = false;
                    }
                    NyamuLogger.LogError($"[Nyamu][Server] AssetDatabase.Refresh failed: {ex.Message}");
                }
            });

            // Return success response immediately (operation queued)
            return Task.FromResult(new RefreshAssetsResponse
            {
                status = "ok",
                message = "Asset database refreshed."
            });
        }
    }
}
