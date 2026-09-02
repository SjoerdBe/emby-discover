using System.IO;
using System.Text;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;

namespace Emby.TmdbDiscover.Configuration
{
    /// <summary>
    /// The view controller for the configuration page, served as an AMD module.
    ///
    /// This exists because of how Emby 4.9 hides the outgoing view during a
    /// navigation. baseview.js does the hiding:
    ///
    ///     oldViewClassList.add("hide"), oldViewClassList.remove("animatedView"),
    ///     newViewClassList.remove("hide", "animatedView")
    ///
    /// but it only gets there through `instance.transitionPromise`, which lives on
    /// the view's *controller*. viewmanager.js then awaits it:
    ///
    ///     ((newView.controller?.transitionPromise) || Promise.resolve()).then(...)
    ///
    /// A page with no controller therefore never runs a transition, so the previous
    /// view never receives the `hide` class and stays on screen underneath (or on
    /// top of) the new page. Registering a controller is what fixes that; Emby's own
    /// plugin pages do the same, e.g. data-controller="__plugin/dlnasettingsjs".
    ///
    /// viewmanager resolves the attribute like this:
    ///     view.startsWith("__plugin/") -> strip the prefix
    ///     pluginmanager.getConfigurationResourceUrl(name)
    ///         -> apiClient.getUrl("web/ConfigurationPage", { name: name })
    ///     require([url]) -> controllerFactory = deps[0]
    ///
    /// so the module must return a *constructor*, invoked as `new Factory(view, params)`.
    /// It must NOT define both onResume and onPause: addMethodsToPluginControllerFactory
    /// only mixes BaseView in when at least one of them is missing, and BaseView is
    /// precisely what provides the transition behaviour we need.
    ///
    /// The name deliberately avoids dots so the AMD loader cannot mistake part of it
    /// for a file extension, and carries the build tag so a plugin update is never
    /// served a stale cached controller.
    /// </summary>
    public sealed class DiscoverConfigScript : IPluginConfigurationPage
    {
        internal static readonly string PageName =
            "discover-for-emby-js-" + DiscoverConfigPage.BuildTag;

        public string Name => PageName;
        public ConfigurationPageType ConfigurationPageType => ConfigurationPageType.None;
        public IPlugin Plugin => TmdbDiscover.Plugin.Instance;

        public Stream GetHtmlStream()
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(ScriptContent));
        }

        // The page's own init code lives in its inert <script type='text/dc-init'>
        // block, so this controller only has to run it -- keeping one copy of the
        // logic. The page also has an <img onerror> fallback that does the same
        // thing, guarded by view.dcInitialized, so the form still works even if this
        // module fails to load (the view is appended before the controller resolves).
        private const string ScriptContent = @"
define([], function () {
    'use strict';

    function DiscoverConfigView(view, params) {
        if (view.dcInitialized) { return; }
        var code = view.querySelector('#dcInitCode');
        if (!code) { return; }
        window.__dcView = view;
        var script = document.createElement('script');
        script.text = code.textContent;
        document.head.appendChild(script);
        script.remove();
    }

    return DiscoverConfigView;
});
";
    }
}
