using System;
using System.IO;
using Emby.TmdbDiscover.Configuration;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;

namespace Emby.TmdbDiscover
{
    public sealed class Plugin : BasePluginSimpleUI<PluginOptions>
    {
        public static Plugin Instance { get; private set; }

        public Plugin(IApplicationHost applicationHost)
            : base(applicationHost)
        {
            Instance = this;
        }

        public override string Name => "Discover for Emby";

        public override string Description =>
            "Browse TMDB trending movies and series inside Emby. " +
            "Watch trailers and add titles to a per-user wishlist.";

        public override Guid Id =>
            new Guid("730fd4a9-537f-481f-b916-87ce952fae56");

        public new PluginOptions GetOptions() => base.GetOptions();
    }
}
