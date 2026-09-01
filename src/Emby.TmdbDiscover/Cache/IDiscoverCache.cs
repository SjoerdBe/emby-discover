using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.TmdbDiscover.Tmdb.Models;

namespace Emby.TmdbDiscover.Cache
{
    public interface IDiscoverCache
    {
        IReadOnlyList<TmdbMovieListItem> GetMovies();
        IReadOnlyList<TmdbTvListItem> GetSeries();
        TmdbMovieListItem FindMovie(int tmdbId);
        TmdbTvListItem FindSeries(int tmdbId);
        CacheMeta GetMeta();
        Task RefreshAsync(CancellationToken cancellationToken);
    }
}
