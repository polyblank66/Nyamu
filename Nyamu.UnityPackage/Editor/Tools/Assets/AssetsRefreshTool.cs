using System;
using System.Threading.Tasks;
using Nyamu.Core.Interfaces;
using UnityEditor;

namespace Nyamu.Tools.Assets
{
    // Tool for refreshing Unity asset database
    public class AssetsRefreshTool : INyamuTool<AssetsRefreshRequest, AssetsRefreshResponse>
    {
        public string Name => "assets_refresh";

        public Task<AssetsRefreshResponse> ExecuteAsync(
            AssetsRefreshRequest request,
            IExecutionContext context)
        {
            var state = context.AssetState;

            // Check if refresh is already in progress
            DateTime refreshRequestTime;
            lock (state.Lock)
            {
                if (state.IsRefreshing)
                {
                    return Task.FromResult(new AssetsRefreshResponse
                    {
                        status = "warning",
                        message = "Asset refresh already in progress. Please wait for current refresh to complete."
                    });
                }

                // Mark refresh as starting
                state.IsRefreshing = true;
                refreshRequestTime = DateTime.Now;
                state.RefreshRequestTime = refreshRequestTime;
                state.RefreshCompletedTime = DateTime.MinValue;
                state.IsWaitingForCompilation = false;
            }

            // Queue the refresh operation on main thread
            context.UnityExecutor.Enqueue(() =>
            {
                try
                {
                    // Store in SessionState (must be on main thread!)
                    SessionState.SetString(Server.SessionKeyRefreshRequestTime, refreshRequestTime.ToString("o"));
                    SessionState.EraseString(Server.SessionKeyRefreshCompletedTime);

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
                catch (Exception ex)
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
            return Task.FromResult(new AssetsRefreshResponse
            {
                status = "ok",
                message = "Asset database refreshed."
            });
        }
    }
}
