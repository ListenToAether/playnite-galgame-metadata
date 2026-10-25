using System;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using GalgameMetadata.Playnite;

namespace GalgameMetadata.Touchgal
{
    /// <summary>Playnite metadata provider for TouchGAL, using the token protected developer API.</summary>
    public class TouchgalPlugin : SiteMetadataPlugin
    {
        public override Guid Id
        {
            get { return new Guid("af145bc3-1883-4933-a1d7-d5124de2cccd"); }
        }

        public TouchgalPlugin(IPlayniteAPI api) : base(api, new TouchgalSource())
        {
        }

        protected override bool ShowApiToken
        {
            get { return true; }
        }

        public override OnDemandMetadataProvider GetMetadataProvider(MetadataRequestOptions options)
        {
            var source = Source as TouchgalSource;
            if (source != null) source.ApiToken = Settings.TouchgalApiToken;
            return base.GetMetadataProvider(options);
        }
    }
}
