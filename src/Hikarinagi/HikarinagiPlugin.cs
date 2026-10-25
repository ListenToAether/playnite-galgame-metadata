using System;
using Playnite.SDK;
using GalgameMetadata.Playnite;

namespace GalgameMetadata.Hikarinagi
{
    /// <summary>Playnite metadata provider for Hikarinagi.</summary>
    public class HikarinagiPlugin : SiteMetadataPlugin
    {
        public override Guid Id
        {
            get { return new Guid("57db3d17-7ad1-4f98-83b3-bcffb08ef8d2"); }
        }

        public HikarinagiPlugin(IPlayniteAPI api) : base(api, new HikarinagiSource())
        {
        }
    }
}
