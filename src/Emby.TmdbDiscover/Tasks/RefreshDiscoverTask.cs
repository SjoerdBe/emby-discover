using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.TmdbDiscover.Cache;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.TmdbDiscover.Tasks
{
    public sealed class RefreshDiscoverTask : IScheduledTask
    {
        private readonly IDiscoverCache _cache;
        private readonly ILogger _logger;

        public RefreshDiscoverTask(IDiscoverCache cache, ILogger logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public string Name => "Refresh Discover from TMDB";
        public string Description => "Fetches latest trending movies and series from TMDB";
        public string Category => "Discover";
        public string Key => "TmdbDiscoverRefresh";

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("Discover: scheduled refresh starting");
            progress.Report(0);

            await _cache.RefreshAsync(cancellationToken).ConfigureAwait(false);

            progress.Report(100);
            _logger.Info("Discover: scheduled refresh complete");
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = TimeSpan.FromHours(12).Ticks
                }
            };
        }
    }
}
