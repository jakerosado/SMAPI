using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using StardewModdingAPI.Toolkit.Framework.Clients.CurseForgeExport;
using StardewModdingAPI.Toolkit.Framework.Clients.CurseForgeExport.ResponseModels;

namespace StardewModdingAPI.Web.Framework.Caching.CurseForgeExport
{
    /// <summary>Manages cached mod data from the CurseForge export API.</summary>
    internal interface ICurseForgeExportCacheRepository : ICacheRepository
    {
        /*********
        ** Methods
        *********/
        /// <summary>Get whether the export data is currently available.</summary>
        bool IsLoaded();

        /// <summary>Get whether newer non-stale data can be fetched from the server.</summary>
        /// <param name="client">The CurseForge API client.</param>
        /// <param name="staleMinutes">The age in minutes before data is considered stale.</param>
        Task<bool> CanRefreshFromAsync(ICurseForgeExportApiClient client, int staleMinutes);

        /// <summary>Get the cached data for a mod, if it exists in the export.</summary>
        /// <param name="id">The CurseForge mod ID.</param>
        /// <param name="mod">The fetched metadata.</param>
        bool TryGetMod(uint id, [NotNullWhen(true)] out CurseForgeModExport? mod);

        /// <summary>Set the cached data to use.</summary>
        /// <param name="export">The export received from the CurseForge Mods API, or <c>null</c> to remove it.</param>
        void SetData(CurseForgeFullExport? export);

        /// <summary>Get whether the cached data is stale.</summary>
        /// <param name="staleMinutes">The age in minutes before data is considered stale.</param>
        bool IsStale(int staleMinutes);
    }
}
