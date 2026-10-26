using System;
using Playnite.SDK;
using GalgameMetadata.Playnite;

namespace GalgameMetadata.Ymgal
{
    /// <summary>Playnite metadata provider for Ymgal (月幕Galgame).</summary>
    public class YmgalPlugin : SiteMetadataPlugin
    {
        public override Guid Id
        {
            get { return new Guid("2460c7c5-5f79-4ebe-826d-cba2084e2464"); }
        }

        public YmgalPlugin(IPlayniteAPI api) : base(api, new YmgalSource())
        {
        }
    }
}
