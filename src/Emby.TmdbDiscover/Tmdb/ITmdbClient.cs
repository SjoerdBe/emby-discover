using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.TmdbDiscover.Tmdb.Models;

namespace Emby.TmdbDiscover.Tmdb
{
    public interface ITmdbClient
    {
        Task<IReadOnlyList<TmdbMovieListItem>> GetTrendingMoviesAsync(
            int desiredCount,
            string window,
            string language,
            string region,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<TmdbTvListItem>> GetTrendingSeriesAsync(
            int desiredCount,
            string window,
            string language,
            string region,
            CancellationToken cancellationToken);

        Task<string> GetTrailerYouTubeKeyAsync(
            string mediaType,
            int tmdbId,
            string language,
            CancellationToken cancellationToken);
    }
}
