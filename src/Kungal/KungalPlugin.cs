using System;
using Playnite.SDK;
using GalgameMetadata.Playnite;

namespace GalgameMetadata.Kungal
{
    /// <summary>Playnite metadata provider for Kungal (鲲galgame), backed by the NextMoe catalog API.</summary>
    public class KungalPlugin : SiteMetadataPlugin
    {
        public override Guid Id
        {
            get { return new Guid("664139ab-88b1-4be5-869e-592b81e21df6"); }
        }

        public KungalPlugin(IPlayniteAPI api) : base(api, new KungalSource())
        {
        }
    }
}
