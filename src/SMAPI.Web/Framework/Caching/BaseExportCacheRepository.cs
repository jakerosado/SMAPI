using System;

namespace StardewModdingAPI.Web.Framework.Caching
{
    /// <summary>The base logic for an export cache repository.</summary>
    internal abstract class BaseExportCacheRepository : BaseCacheRepository, IExportCacheRepository
    {
        /*********
         ** Public methods
         *********/
        /// <inheritdoc />
        public abstract bool IsLoaded();

        /// <inheritdoc />
        public abstract DateTimeOffset GetLastModified();

        /// <inheritdoc />
        public bool IsStale(int staleMinutes)
        {
            return this.IsStale(this.GetLastModified(), staleMinutes);
        }

        /// <inheritdoc />
        public abstract void Clear();
    }
}
