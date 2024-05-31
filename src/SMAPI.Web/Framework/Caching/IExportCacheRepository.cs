using System;

namespace StardewModdingAPI.Web.Framework.Caching
{
    /// <summary>Encapsulates logic for accessing data in a cached mod export from a remote API.</summary>
    internal interface IExportCacheRepository : ICacheRepository
    {
        /*********
        ** Methods
        *********/
        /// <summary>Get whether the export data is currently available.</summary>
        bool IsLoaded();

        /// <summary>Get the date when the cached data was last modified.</summary>
        DateTimeOffset GetLastModified();

        /// <summary>Get whether the cached data is stale.</summary>
        /// <param name="staleMinutes">The age in minutes before data is considered stale.</param>
        bool IsStale(int staleMinutes);

        /// <summary>Clear all data in the cache.</summary>
        void Clear();
    }
}
