using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Emby.TmdbDiscover.Tasks
{
    public sealed class RefreshDiscoverTask : IScheduledTask
    {
        public string Name => "Refresh Discover from TMDB";
        public string Description => "Fetches latest trending movies and series from TMDB";
        public string Category => "Discover";
        public string Key => "TmdbDiscoverRefresh";

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var logger = Plugin.Instance.GetLogger();
            logger.Info("Discover: scheduled refresh starting");
            progress.Report(0);

            await Plugin.Instance.GetDiscoverCache().RefreshAsync(cancellationToken).ConfigureAwait(false);
            progress.Report(80);

            await Plugin.Instance.UpdateChannelItemTagsAsync(cancellationToken).ConfigureAwait(false);

            progress.Report(100);
            logger.Info("Discover: scheduled refresh complete");
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
