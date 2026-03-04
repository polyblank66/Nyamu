using System;

namespace Nyamu.Tools.Assets
{
    // Request DTO for asset refresh
    [Serializable]
    public class AssetsRefreshRequest
    {
        public bool force;
    }
}
