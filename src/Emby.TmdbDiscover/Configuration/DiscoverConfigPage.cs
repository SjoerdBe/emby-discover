using System.IO;
using System.Text;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;

namespace Emby.TmdbDiscover.Configuration
{
    public sealed class DiscoverConfigPage : IPluginConfigurationPage
    {
        // CACHE BUSTING -------------------------------------------------------------
        // Emby fetches this page from /web/configurationpage?name=<Name>&v=<EMBY
        // version> and the response carries `cache-control: public` with an ETag.
        // The `v` is the *server's* version, so it does not change when this plugin
        // is updated -- a browser that has seen the page once keeps serving the old
        // HTML indefinitely, even across an Emby restart and a hard refresh. Users
        // would have to clear their cache by hand after every plugin update.
        //
        // The page's Name is the only part of that URL we control, and it is purely
        // a URL slug: Emby reports a separate DisplayName (taken from Plugin.Name)
        // for anything user-facing, which is how every other plugin ends up with
        // slugs like "embybackup" -> "Backup & Restore". So the slug carries a
        // build-unique tag and the URL changes whenever the plugin does.
        //
        // The tag is the assembly version plus a short ModuleVersionId. The version
        // covers releases; the MVID changes on every compile, so local rebuilds bust
        // the cache too without needing a version bump to test a page change.
        // ---------------------------------------------------------------------------

        private static readonly string VersionString =
            typeof(DiscoverConfigPage).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        internal static readonly string BuildTag =
            typeof(DiscoverConfigPage).Assembly.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 8);

        private static readonly string CacheTag = VersionString + "-" + BuildTag;

        public string Name => "discover-for-emby-" + CacheTag;
        public ConfigurationPageType ConfigurationPageType => ConfigurationPageType.PluginConfiguration;
        public IPlugin Plugin => TmdbDiscover.Plugin.Instance;

        public Stream GetHtmlStream()
        {
            var html = PageHtml
                .Replace("{{VERSION}}", VersionString)
                .Replace("{{CONTROLLER}}", DiscoverConfigScript.PageName);
            return new MemoryStream(Encoding.UTF8.GetBytes(html));
        }

        // NOTE ON THE BOOTSTRAP BELOW ------------------------------------------------
        // Emby 4.9's viewmanager.js injects a plugin configuration page with:
        //     div = document.createElement('div'); div.innerHTML = html;
        //     view = div.querySelector('.view,div[data-role=page]');
        // Per the HTML spec, <script> elements produced by the innerHTML fragment
        // parser are flagged ""already started"" and NEVER execute -- not when parsed
        // and not when the element is later appended to the document. Emby has no
        // code path anywhere in the web client that re-creates or re-executes those
        // script elements. That is why every inline <script> on this page was dead.
        //
        // Inline event-handler ATTRIBUTES are not affected by that rule: they are
        // compiled as normal event handlers on innerHTML-parsed elements. The hidden
        // <img> below therefore fires its onerror handler (its data: URI is not a
        // decodable image), and that handler copies the code out of the inert
        // <script type='text/dc-init'> block into a script element created with
        // document.createElement -- dynamically created scripts DO execute.
        //
        // The 'data-controller' route (a second IPluginConfigurationPage serving an
        // AMD module) is deliberately NOT used: if that module 404s or throws, the
        // require() promise in viewmanager rejects and onViewChange never runs, which
        // silently kills the page with no error surfaced. This bootstrap has no
        // second endpoint and no loader dependency.
        // ---------------------------------------------------------------------------

        private const string PageHtml = @"
<div id='discoverConfigPage' is='emby-scroller'
     class='view flex flex-direction-column scrollFrameY flex-grow'
     data-horizontal='false' data-forcescrollbar='true' data-centerfocus='true'
     data-bindheader='true' data-title='Discover for Emby'
     data-controller='__plugin/{{CONTROLLER}}'>

<div class='scrollSlider flex-grow flex-direction-column padded-left padded-left-page padded-right padded-top-page padded-bottom-page'>

<img id='dcBoot' src='data:,' alt='' aria-hidden='true' style='display:none;width:0;height:0'
     onerror=""var v=this.closest('#discoverConfigPage');if(!v){return;}var c=v.querySelector('#dcInitCode');if(!c){return;}window.__dcView=v;var s=document.createElement('script');s.text=c.textContent;document.head.appendChild(s);s.remove();this.remove();""
     onload=""this.onerror&&this.onerror(event)"">

<script type='text/dc-init' id='dcInitCode'>
(function () {
    'use strict';

    var view = window.__dcView;
    window.__dcView = null;
    if (!view || view.dcInitialized) { return; }
    view.dcInitialized = true;

    console.log('[Discover] config page script running');

    var fields = {
        TmdbApiKey:              { el: 'optTmdbApiKey',             type: 'text' },
        Language:                { el: 'optLanguage',               type: 'text' },
        Region:                  { el: 'optRegion',                 type: 'text' },
        TrendingWindow:          { el: 'optTrendingWindow',         type: 'text' },
        ChannelName:             { el: 'optChannelName',            type: 'text' },
        EnableMovies:            { el: 'optEnableMovies',           type: 'check' },
        MovieCount:              { el: 'optMovieCount',             type: 'number' },
        EnableSeries:            { el: 'optEnableSeries',           type: 'check' },
        SeriesCount:             { el: 'optSeriesCount',            type: 'number' },
        HideLibraryItems:        { el: 'optHideLibraryItems',       type: 'check' },
        NotifyOnWishlistAdd:     { el: 'optNotifyOnWishlistAdd',    type: 'check' },
        SmtpHost:                { el: 'optSmtpHost',               type: 'text' },
        SmtpPort:                { el: 'optSmtpPort',               type: 'number' },
        SmtpUseSsl:              { el: 'optSmtpUseSsl',             type: 'check' },
        SmtpUsername:            { el: 'optSmtpUsername',           type: 'text' },
        SmtpPassword:            { el: 'optSmtpPassword',           type: 'text' },
        NotificationFromEmail:   { el: 'optNotificationFromEmail',  type: 'text' },
        NotificationToEmail:     { el: 'optNotificationToEmail',    type: 'text' },
        NotificationWebhookUrl:  { el: 'optNotificationWebhookUrl', type: 'text' }
    };

    // ApiClient is published by legacy/dashboard.js, which viewmanager loads in
    // parallel with this page, so it may not be there for the very first call.
    function withApiClient(callback) {
        var client = window.ApiClient;
        if (client && client.getUrl && client.accessToken) { callback(client); return; }
        setTimeout(function () { withApiClient(callback); }, 100);
    }

    function request(method, path, body) {
        return new Promise(function (resolve, reject) {
            withApiClient(function (client) {
                var init = { method: method, headers: { 'X-Emby-Token': client.accessToken() } };
                if (body !== undefined && body !== null) {
                    init.headers['Content-Type'] = 'application/json';
                    init.body = JSON.stringify(body);
                }
                fetch(client.getUrl('TmdbDiscover/' + path), init).then(function (response) {
                    if (!response.ok) {
                        reject(new Error('HTTP ' + response.status + ' ' + response.statusText));
                        return;
                    }
                    response.text().then(function (text) {
                        if (!text) { resolve(null); return; }
                        var parsed;
                        try { parsed = JSON.parse(text); } catch (e) { parsed = text; }
                        resolve(parsed);
                    }, reject);
                }, reject);
            });
        });
    }

    function el(id) { return view.querySelector('#' + id); }

    // kind: 'ok' | 'err' | 'info'
    function showToast(id, kind, text) {
        var node = el(id);
        if (!node) { return; }
        node.className = 'dc-toast ' + (kind === true ? 'ok' : kind === false ? 'err' : kind);
        node.textContent = text;
    }

    function hideToast(id) {
        var node = el(id);
        if (node) { node.className = 'dc-toast'; }
    }

    function errorText(err) {
        return (err && (err.message || err.statusText)) || 'unknown error';
    }

    var loading = false;
    var lastLoad = 0;

    function loadOptions() {
        // The first viewshow arrives right after the bootstrap ran, so collapse
        // those two into a single GET; later re-shows still refresh.
        var now = Date.now();
        if (loading || now - lastLoad < 1000) { return; }
        loading = true;
        lastLoad = now;
        request('GET', 'Options').then(function (opts) {
            loading = false;
            if (!opts) { return; }
            for (var key in fields) {
                if (!Object.prototype.hasOwnProperty.call(fields, key)) { continue; }
                var field = fields[key];
                var node = el(field.el);
                if (!node || opts[key] === undefined || opts[key] === null) { continue; }
                if (field.type === 'check') { node.checked = !!opts[key]; }
                else { node.value = opts[key]; }
            }
        }, function (err) {
            loading = false;
            showToast('saveResult', false, 'Failed to load settings: ' + errorText(err));
        });
    }

    function collectOptions() {
        var data = {};
        for (var key in fields) {
            if (!Object.prototype.hasOwnProperty.call(fields, key)) { continue; }
            var field = fields[key];
            var node = el(field.el);
            if (!node) { continue; }
            if (field.type === 'check') { data[key] = node.checked; }
            else if (field.type === 'number') { data[key] = parseInt(node.value, 10) || 0; }
            else { data[key] = node.value; }
        }
        return data;
    }

    function bind(id, busyLabel, handler) {
        var button = el(id);
        if (!button) { return; }
        var label = button.textContent;
        button.addEventListener('click', function () {
            button.disabled = true;
            button.textContent = busyLabel;
            handler().then(null, function () { }).then(function () {
                button.disabled = false;
                button.textContent = label;
            });
        });
    }

    bind('btnSave', 'Saving...', function () {
        hideToast('saveResult');
        return request('POST', 'Options', collectOptions()).then(function () {
            showToast('saveResult', true, 'Settings saved.');
        }, function (err) {
            showToast('saveResult', false, 'Save failed: ' + errorText(err));
        });
    });

    bind('btnTestNotification', 'Sending...', function () {
        hideToast('testEmailResult');
        hideToast('testWebhookResult');
        // Send the live form values so Test reflects what is on screen, not what
        // was last saved -- otherwise typing settings and pressing Test silently
        // tests the old saved ones, which is thoroughly confusing.
        var current = collectOptions();
        return request('POST', 'TestNotification', {
            SmtpHost: current.SmtpHost,
            SmtpPort: current.SmtpPort,
            SmtpUseSsl: current.SmtpUseSsl,
            SmtpUsername: current.SmtpUsername,
            SmtpPassword: current.SmtpPassword,
            NotificationFromEmail: current.NotificationFromEmail,
            NotificationToEmail: current.NotificationToEmail,
            NotificationWebhookUrl: current.NotificationWebhookUrl
        }).then(function (result) {
            result = result || {};
            var kind = function (ok, configured) { return !configured ? 'info' : (ok ? 'ok' : 'err'); };
            showToast('testEmailResult', kind(result.EmailSuccess, result.EmailConfigured),
                'Email: ' + (result.EmailMessage || 'no response'));
            showToast('testWebhookResult', kind(result.WebhookSuccess, result.WebhookConfigured),
                'Webhook: ' + (result.WebhookMessage || 'no response'));
            if (result.UsedUnsavedSettings && (result.EmailSuccess || result.WebhookSuccess)) {
                showToast('saveResult', 'info',
                    'Tested with the values on screen. Press Save Settings to keep them.');
            }
        }, function (err) {
            showToast('testEmailResult', false, 'Request failed: ' + errorText(err));
        });
    });

    bind('btnRefresh', 'Refreshing...', function () {
        hideToast('refreshResult');
        return request('POST', 'Refresh').then(function (result) {
            result = result || {};
            if (result.Success) {
                showToast('refreshResult', true,
                    'Refreshed: ' + result.MovieCount + ' movies, ' + result.SeriesCount + ' series');
            } else {
                showToast('refreshResult', false, 'Error: ' + (result.Error || 'unknown error'));
            }
        }, function (err) {
            showToast('refreshResult', false, 'Request failed: ' + errorText(err));
        });
    });

    // Emby caches and re-shows view elements, so re-read settings on every show.
    view.addEventListener('viewshow', loadOptions);
    loadOptions();
})();
</script>

<form autocomplete='off' id='dcForm'>
<input type='text' name='fakeuser' style='position:absolute;left:-9999px' tabindex='-1' aria-hidden='true'>
<input type='password' name='fakepass' style='position:absolute;left:-9999px' tabindex='-1' aria-hidden='true'>

<style>
    /* Colours come from Emby's own theme custom properties (see
       modules/themes/<theme>/theme.css) so this page follows whatever theme the
       user has selected. Hardcoding colours here breaks badly: an
       rgba(0,0,0,0.2) field background reads as a disabled control on the light
       theme. Each var() carries a fallback in case a theme omits it.
       Emby's own rule is:
         .emby-input { background: hsla(var(--input-background-*), var(--button-background-alpha));
                       border: var(--line-size) solid var(--line-background); } */

    .dc-page { max-width: 700px; }

    .dc-card {
        background: hsla(var(--card-background-hue, 0), var(--card-background-saturation, 0%),
                         var(--card-background-lightness, 50%), var(--card-background-alpha, 0.08));
        border: var(--line-size, 0.08em) solid var(--line-background, rgba(128,128,128,0.25));
        border-radius: 0.5em; padding: 1.2em 1.5em; margin-bottom: 1.2em;
    }
    .dc-card h3 { margin: 0 0 0.8em; font-size: 1.1em; }

    .dc-field { margin-bottom: 1em; }
    .dc-field:last-child { margin-bottom: 0; }
    .dc-label { display: block; margin-bottom: 0.3em; font-weight: 600; font-size: 0.95em; }
    .dc-hint {
        display: block; margin-bottom: 0.4em; font-size: 0.85em;
        color: hsla(var(--theme-text-color-hue, 0), var(--theme-text-color-saturation, 0%),
                    var(--theme-text-color-lightness, 50%), var(--theme-secondary-text-color-alpha, 0.56));
    }

    .dc-input, .dc-select {
        width: 100%; height: 3em; padding: 0 1ch; box-sizing: border-box;
        color: inherit; font-size: inherit; font-family: inherit;
        background: hsla(var(--input-background-hue, 0), var(--input-background-saturation, 0%),
                         var(--input-background-lightness, 100%), var(--button-background-alpha, 1));
        border: var(--line-size, 0.08em) solid var(--line-background, rgba(128,128,128,0.35));
        border-radius: 0.3em;
    }
    .dc-input:focus-visible, .dc-select:focus-visible {
        outline: none; border-color: transparent;
        box-shadow: 0 0 0 0.2rem hsla(var(--theme-primary-color-hue, 116),
                                      var(--theme-primary-color-saturation, 42%),
                                      var(--theme-primary-color-lightness, 50%), 0.6);
    }
    .dc-select { width: auto; padding-inline-end: 0.5ch; }
    .dc-input-short { max-width: 200px; }
    .dc-input-medium { max-width: 400px; }

    /* ALIGNMENT: grid cells are bottom-aligned with align-items:end, which aligns
       *margin boxes*. .dc-field carries margin-bottom:1em that .dc-field:last-child
       removes, so every cell except the last sat 1em higher -- which is why the
       trailing Window and SSL controls hung low. Inside a grid the margin is
       dropped entirely and vertical spacing comes from the grid's own row gap. */
    .dc-grid2, .dc-grid3, .dc-grid-smtp { display: grid; gap: 1em 1.5em; align-items: end; }
    .dc-grid2 { grid-template-columns: 1fr 1fr; }
    .dc-grid3 { grid-template-columns: 1fr 1fr auto; }
    .dc-grid-smtp { grid-template-columns: 2fr auto auto; }
    .dc-grid2 > .dc-field, .dc-grid3 > .dc-field, .dc-grid-smtp > .dc-field {
        margin-bottom: 0; display: flex; flex-direction: column; justify-content: flex-end;
    }

    .dc-check { display: flex; align-items: center; gap: 0.5em; margin-bottom: 0.6em; }
    .dc-check label { font-weight: 400; cursor: pointer; }
    .dc-check input[type=checkbox] { width: 18px; height: 18px; cursor: pointer; margin: 0; }
    /* Gives a checkbox the same box height as an input so it bottom-aligns in a grid row. */
    .dc-check-aligned { height: 3em; margin-bottom: 0; }

    .dc-actions { display: flex; gap: 0.6em; flex-wrap: wrap; }
    .dc-btn {
        padding: 0 1.5em; height: 3em; border: none; border-radius: 0.3em; cursor: pointer;
        font-size: inherit; font-family: inherit; font-weight: 600;
    }
    .dc-btn:disabled { opacity: 0.5; cursor: default; }
    .dc-btn-save {
        color: #fff;
        background: hsl(var(--theme-primary-color-hue, 116), var(--theme-primary-color-saturation, 42%),
                        var(--theme-primary-color-lightness, 50%));
    }
    .dc-btn-action {
        color: inherit;
        background: hsla(var(--button-background-hue, 240), var(--button-background-saturation, 24%),
                         var(--button-background-lightness, 94%), var(--button-background-alpha, 1));
        border: var(--line-size, 0.08em) solid var(--line-background, rgba(128,128,128,0.25));
    }
    .dc-btn:not(:disabled):hover { filter: brightness(0.93); }

    .dc-toast {
        margin-top: 0.6em; padding: 0.6em 0.9em; border-radius: 0.3em;
        font-size: 0.9em; display: none;
    }
    .dc-toast.ok   { background: #2e7d32; color: #fff; display: block; }
    .dc-toast.err  { background: #c62828; color: #fff; display: block; }
    /* Something simply not set up is not a failure, so it must not look like one. */
    .dc-toast.info {
        display: block; color: inherit;
        background: hsla(var(--button-background-hue, 240), var(--button-background-saturation, 24%),
                         var(--button-background-lightness, 94%), var(--button-background-alpha, 1));
        border: var(--line-size, 0.08em) solid var(--line-background, rgba(128,128,128,0.25));
    }

    .dc-sep {
        margin-top: 1em; padding-top: 1em;
        border-top: var(--line-size, 0.08em) solid var(--line-background, rgba(128,128,128,0.2));
    }
    .dc-sep-label { margin-bottom: 0.6em; font-weight: 600; font-size: 0.95em; }

    @media (max-width: 500px) {
        .dc-grid2, .dc-grid3, .dc-grid-smtp { grid-template-columns: 1fr; }
        .dc-check-aligned { height: auto; }
    }
</style>

<div class='dc-page'>

<h2 style='margin-bottom: 0.2em'>Discover for Emby</h2>
<div style='margin-bottom: 1em; opacity: 0.5; font-size: 0.85em'>Version {{VERSION}}</div>

<!-- TMDB -->
<div class='dc-card'>
    <h3>TMDB</h3>
    <div class='dc-field'>
        <label class='dc-label'>API Key</label>
        <span class='dc-hint'>Your TMDB v3 API key. Never written to logs.</span>
        <input type='text' id='optTmdbApiKey' class='dc-input' autocomplete='off' data-lpignore='true'>
    </div>
    <div class='dc-grid3'>
        <div class='dc-field'>
            <label class='dc-label'>Language</label>
            <span class='dc-hint'>e.g. en-GB, nl-NL</span>
            <input type='text' id='optLanguage' class='dc-input' autocomplete='off'>
        </div>
        <div class='dc-field'>
            <label class='dc-label'>Region</label>
            <span class='dc-hint'>e.g. NL, GB, US</span>
            <input type='text' id='optRegion' class='dc-input' autocomplete='off'>
        </div>
        <div class='dc-field'>
            <label class='dc-label'>Window</label>
            <span class='dc-hint'>&nbsp;</span>
            <select id='optTrendingWindow' class='dc-select'>
                <option value='Day'>Day</option>
                <option value='Week'>Week</option>
            </select>
        </div>
    </div>
</div>

<!-- Channel -->
<div class='dc-card'>
    <h3>Channel</h3>
    <div class='dc-field'>
        <label class='dc-label'>Channel Name</label>
        <input type='text' id='optChannelName' class='dc-input' autocomplete='off'>
    </div>
    <div class='dc-grid2'>
        <div class='dc-field'>
            <div class='dc-check'>
                <input type='checkbox' id='optEnableMovies'>
                <label for='optEnableMovies'>Trending Movies</label>
            </div>
            <label class='dc-label'>Count</label>
            <input type='number' id='optMovieCount' class='dc-input dc-input-short' min='20' max='100' step='20' autocomplete='off'>
        </div>
        <div class='dc-field'>
            <div class='dc-check'>
                <input type='checkbox' id='optEnableSeries'>
                <label for='optEnableSeries'>Trending Series</label>
            </div>
            <label class='dc-label'>Count</label>
            <input type='number' id='optSeriesCount' class='dc-input dc-input-short' min='20' max='100' step='20' autocomplete='off'>
        </div>
    </div>
    <div class='dc-check' style='margin-top: 0.4em'>
        <input type='checkbox' id='optHideLibraryItems'>
        <label for='optHideLibraryItems'>Hide items already in library</label>
    </div>
</div>

<!-- Notifications -->
<div class='dc-card'>
    <h3>Notifications</h3>
    <div class='dc-check'>
        <input type='checkbox' id='optNotifyOnWishlistAdd'>
        <label for='optNotifyOnWishlistAdd'>Notify when a user favorites a Discover item</label>
    </div>

    <div class='dc-sep'>
        <div class='dc-sep-label'>Email (SMTP)</div>
        <div class='dc-grid-smtp'>
            <div class='dc-field'>
                <label class='dc-label'>Server</label>
                <input type='text' id='optSmtpHost' class='dc-input' autocomplete='off' placeholder='smtp.gmail.com'>
            </div>
            <div class='dc-field'>
                <label class='dc-label'>Port</label>
                <input type='number' id='optSmtpPort' class='dc-input' style='width:80px' autocomplete='off'>
            </div>
            <div class='dc-field'>
                <label class='dc-label'>&nbsp;</label>
                <div class='dc-check dc-check-aligned'>
                    <input type='checkbox' id='optSmtpUseSsl'>
                    <label for='optSmtpUseSsl'>SSL</label>
                </div>
            </div>
        </div>
        <div class='dc-grid2'>
            <div class='dc-field'>
                <label class='dc-label'>Username</label>
                <input type='text' id='optSmtpUsername' name='dc_smtp_user' class='dc-input' autocomplete='off' data-lpignore='true' data-form-type='other'>
            </div>
            <div class='dc-field'>
                <label class='dc-label'>Password</label>
                <input type='password' id='optSmtpPassword' name='dc_smtp_pass' class='dc-input' autocomplete='new-password' data-lpignore='true' data-form-type='other'>
            </div>
        </div>
        <div class='dc-grid2'>
            <div class='dc-field'>
                <label class='dc-label'>From</label>
                <input type='email' id='optNotificationFromEmail' class='dc-input' autocomplete='off' placeholder='sender@example.com'>
            </div>
            <div class='dc-field'>
                <label class='dc-label'>To</label>
                <input type='email' id='optNotificationToEmail' class='dc-input' autocomplete='off' placeholder='you@example.com'>
            </div>
        </div>
    </div>

    <div class='dc-sep'>
        <div class='dc-sep-label'>Webhook (optional)</div>
        <div class='dc-field'>
            <input type='url' id='optNotificationWebhookUrl' class='dc-input' autocomplete='off' placeholder='https://ntfy.sh/your-topic'>
        </div>
    </div>
</div>

<!-- Actions -->
<div class='dc-card'>
    <div class='dc-actions'>
        <button type='button' class='dc-btn dc-btn-save' id='btnSave'>Save Settings</button>
        <button type='button' class='dc-btn dc-btn-action' id='btnTestNotification'>Test Notification</button>
        <button type='button' class='dc-btn dc-btn-action' id='btnRefresh'>Refresh Channel</button>
    </div>
    <div id='saveResult' class='dc-toast'></div>
    <div id='testEmailResult' class='dc-toast'></div>
    <div id='testWebhookResult' class='dc-toast'></div>
    <div id='refreshResult' class='dc-toast'></div>
</div>

</div><!-- dc-page -->
</form>

</div><!-- scrollSlider -->
</div><!-- view -->";
    }
}
