using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.TmdbDiscover.Wishlist
{
    public sealed class JsonWishlistStore : IWishlistStore
    {
        private readonly string _filePath;
        private readonly IJsonSerializer _json;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private List<WishlistEntry> _entries = new List<WishlistEntry>();

        public JsonWishlistStore(string dataPath, IJsonSerializer json, ILogger logger)
        {
            _filePath = Path.Combine(dataPath, "wishlist.json");
            _json = json;
            _logger = logger;
            Load();
        }

        public void Upsert(WishlistEntry entry)
        {
            _lock.Wait();
            try
            {
                var existing = _entries.FirstOrDefault(e =>
                    e.UserId == entry.UserId &&
                    e.MediaType == entry.MediaType &&
                    e.TmdbId == entry.TmdbId);

                if (existing != null)
                {
                    existing.IsActive = true;
                    existing.RemovedAtUtc = null;
                    existing.Title = entry.Title ?? existing.Title;
                    existing.OriginalTitle = entry.OriginalTitle ?? existing.OriginalTitle;
                    existing.Year = entry.Year ?? existing.Year;
                    existing.PosterPath = entry.PosterPath ?? existing.PosterPath;
                    existing.UserNameSnapshot = entry.UserNameSnapshot ?? existing.UserNameSnapshot;
                    if (existing.AddedAtUtc == default)
                        existing.AddedAtUtc = DateTimeOffset.UtcNow;
                }
                else
                {
                    entry.IsActive = true;
                    entry.AddedAtUtc = DateTimeOffset.UtcNow;
                    _entries.Add(entry);
                }

                Save();
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Deactivate(long userId, string mediaType, int tmdbId)
        {
            _lock.Wait();
            try
            {
                var entry = _entries.FirstOrDefault(e =>
                    e.UserId == userId && e.MediaType == mediaType &&
                    e.TmdbId == tmdbId && e.IsActive);

                if (entry != null)
                {
                    entry.IsActive = false;
                    entry.RemovedAtUtc = DateTimeOffset.UtcNow;
                    Save();
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Remove(long userId, string mediaType, int tmdbId)
        {
            _lock.Wait();
            try
            {
                _entries.RemoveAll(e =>
                    e.UserId == userId && e.MediaType == mediaType && e.TmdbId == tmdbId);
                Save();
            }
            finally
            {
                _lock.Release();
            }
        }

        public IReadOnlyList<WishlistEntry> GetActive(long? userId = null)
        {
            var snapshot = _entries.ToList();
            return snapshot
                .Where(e => e.IsActive && (!userId.HasValue || e.UserId == userId.Value))
                .OrderByDescending(e => e.AddedAtUtc)
                .ToList();
        }

        public IReadOnlyList<WishlistEntry> GetAll(long? userId = null)
        {
            var snapshot = _entries.ToList();
            return snapshot
                .Where(e => !userId.HasValue || e.UserId == userId.Value)
                .OrderByDescending(e => e.AddedAtUtc)
                .ToList();
        }

        public WishlistEntry Find(long userId, string mediaType, int tmdbId)
        {
            return _entries.FirstOrDefault(e =>
                e.UserId == userId && e.MediaType == mediaType && e.TmdbId == tmdbId);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;

                using (var stream = File.OpenRead(_filePath))
                {
                    var loaded = _json.DeserializeFromStream<List<WishlistEntry>>(stream);
                    if (loaded != null)
                    {
                        _entries = loaded;
                        _logger.Info("Discover: loaded {0} wishlist entries", _entries.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Discover: failed to load wishlist: {0}", ex.Message);
                try
                {
                    var corrupt = _filePath + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    File.Move(_filePath, corrupt);
                    _logger.Warn("Discover: renamed corrupt wishlist to {0}", corrupt);
                }
                catch { }
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var tmp = _filePath + ".tmp";
                using (var stream = File.Create(tmp))
                {
                    _json.SerializeToStream(_entries, stream);
                }

                if (File.Exists(_filePath)) File.Delete(_filePath);
                File.Move(tmp, _filePath);
            }
            catch (Exception ex)
            {
                _logger.Error("Discover: failed to save wishlist: {0}", ex.Message);
            }
        }
    }
}
