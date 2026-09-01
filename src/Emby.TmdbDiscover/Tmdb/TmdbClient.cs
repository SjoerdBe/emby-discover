using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emby.TmdbDiscover.Tmdb.Models;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.TmdbDiscover.Tmdb
{
    public sealed class TmdbClient : ITmdbClient
    {
        private readonly IHttpClient _httpClient;
        private readonly IJsonSerializer _json;
        private readonly ILogger _logger;
        private readonly Func<string> _getApiKey;

        public TmdbClient(
            IHttpClient httpClient,
            IJsonSerializer json,
            ILogger logger,
            Func<string> getApiKey)
        {
            _httpClient = httpClient;
            _json = json;
            _logger = logger;
            _getApiKey = getApiKey;
        }

        public async Task<IReadOnlyList<TmdbMovieListItem>> GetTrendingMoviesAsync(
            int desiredCount, string window, string language, CancellationToken cancellationToken)
        {
            var pages = (int)Math.Ceiling(desiredCount / 20d);
            var output = new List<TmdbMovieListItem>();
            var seen = new HashSet<int>();

            for (var page = 1; page <= pages; page++)
            {
                var url = TmdbUrls.TrendingMovies(window, page, language, _getApiKey());
                var response = await FetchPage<TmdbMovieListItem>(url, cancellationToken).ConfigureAwait(false);
                if (response?.Results == null) break;

                _logger.Debug("Discover: fetched movie page {0}/{1}, {2} results",
                    page, pages, response.Results.Count);

                foreach (var item in response.Results)
                {
                    if (item.Adult) continue;
                    if (!seen.Add(item.Id)) continue;
                    output.Add(item);
                    if (output.Count >= desiredCount) return output;
                }

                if (page >= response.TotalPages) break;
            }

            return output;
        }

        public async Task<IReadOnlyList<TmdbTvListItem>> GetTrendingSeriesAsync(
            int desiredCount, string window, string language, CancellationToken cancellationToken)
        {
            var pages = (int)Math.Ceiling(desiredCount / 20d);
            var output = new List<TmdbTvListItem>();
            var seen = new HashSet<int>();

            for (var page = 1; page <= pages; page++)
            {
                var url = TmdbUrls.TrendingSeries(window, page, language, _getApiKey());
                var response = await FetchPage<TmdbTvListItem>(url, cancellationToken).ConfigureAwait(false);
                if (response?.Results == null) break;

                _logger.Debug("Discover: fetched series page {0}/{1}, {2} results",
                    page, pages, response.Results.Count);

                foreach (var item in response.Results)
                {
                    if (item.Adult) continue;
                    if (!seen.Add(item.Id)) continue;
                    output.Add(item);
                    if (output.Count >= desiredCount) return output;
                }

                if (page >= response.TotalPages) break;
            }

            return output;
        }

        private async Task<TmdbPagedResponse<T>> FetchPage<T>(string url, CancellationToken cancellationToken)
        {
            const int maxRetries = 2;
            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var options = new HttpRequestOptions
                    {
                        Url = url,
                        CancellationToken = cancellationToken,
                        TimeoutMs = 15000
                    };

                    using (var response = await _httpClient.GetResponse(options).ConfigureAwait(false))
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                            response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            _logger.Error("Discover: TMDB authentication failed ({0})", response.StatusCode);
                            return null;
                        }

                        if ((int)response.StatusCode == 429)
                        {
                            _logger.Warn("Discover: TMDB rate limited (429)");
                            return null;
                        }

                        if (response.StatusCode != System.Net.HttpStatusCode.OK)
                        {
                            _logger.Warn("Discover: TMDB returned {0} for {1}",
                                response.StatusCode, TmdbUrls.RedactedUrl(url));
                            if (attempt < maxRetries)
                            {
                                await Task.Delay(1000 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                            return null;
                        }

                        return _json.DeserializeFromStream<TmdbPagedResponse<T>>(response.Content);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.Warn("Discover: TMDB request failed (attempt {0}): {1}",
                        attempt + 1, ex.Message);
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(1000 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            return null;
        }
    }
}
