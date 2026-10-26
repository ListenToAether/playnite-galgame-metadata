using System;
using Playnite.SDK;
using GalgameMetadata.Playnite;

namespace GalgameMetadata.Shionlib
{
    /// <summary>Playnite metadata provider for Shionlib (书音的图书馆).</summary>
    public class ShionlibPlugin : SiteMetadataPlugin
    {
        public override Guid Id
        {
            get { return new Guid("e882fba8-217a-4fcd-a400-b53b3392a734"); }
        }

        public ShionlibPlugin(IPlayniteAPI api) : base(api, new ShionlibSource())
        {
        }
    }
}
