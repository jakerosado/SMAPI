using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Humanizer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StardewModdingAPI.Toolkit;
using StardewModdingAPI.Toolkit.Framework.Clients.CurseForgeExport;
using StardewModdingAPI.Toolkit.Framework.Clients.NexusExport;
using StardewModdingAPI.Toolkit.Framework.Clients.Wiki;
using StardewModdingAPI.Web.Framework.Caching;
using StardewModdingAPI.Web.Framework.Caching.CurseForgeExport;
using StardewModdingAPI.Web.Framework.Caching.Mods;
using StardewModdingAPI.Web.Framework.Caching.NexusExport;
using StardewModdingAPI.Web.Framework.Caching.Wiki;
using StardewModdingAPI.Web.Framework.Clients.CurseForge;
using StardewModdingAPI.Web.Framework.Clients.Nexus;
using StardewModdingAPI.Web.Framework.ConfigModels;

namespace StardewModdingAPI.Web
{
    /// <summary>A hosted service which runs background data updates.</summary>
    /// <remarks>Task methods need to be static, since otherwise Hangfire will try to serialize the entire instance.</remarks>
    internal class BackgroundService : IHostedService, IDisposable
    {
        /*********
        ** Fields
        *********/
        /// <summary>The background task server.</summary>
        private static BackgroundJobServer? JobServer;

        /// <summary>The cache in which to store wiki metadata.</summary>
        private static IWikiCacheRepository? WikiCache;

        /// <summary>The cache in which to store mod data.</summary>
        private static IModCacheRepository? ModCache;

        /// <summary>The HTTP client for fetching the mod export from the CurseForge export API.</summary>
        private static ICurseForgeExportApiClient? CurseForgeExportApiClient;

        /// <summary>The HTTP client for fetching the mod export from the CurseForge export API.</summary>
        private static ICurseForgeExportCacheRepository? CurseForgeExportCache;

        /// <summary>The cache in which to store mod data from the Nexus export API.</summary>
        private static INexusExportCacheRepository? NexusExportCache;

        /// <summary>The HTTP client for fetching the mod export from the Nexus Mods export API.</summary>
        private static INexusExportApiClient? NexusExportApiClient;

        /// <summary>The config settings for mod update checks.</summary>
        private static IOptions<ModUpdateCheckConfig>? UpdateCheckConfig;

        /// <summary>Whether the service has been started.</summary>
        [MemberNotNullWhen(true,
            nameof(BackgroundService.JobServer),
            nameof(BackgroundService.ModCache),
            nameof(BackgroundService.CurseForgeExportApiClient),
            nameof(BackgroundService.CurseForgeExportCache),
            nameof(BackgroundService.NexusExportApiClient),
            nameof(BackgroundService.NexusExportCache),
            nameof(BackgroundService.UpdateCheckConfig),
            nameof(BackgroundService.WikiCache)
        )]
        private static bool IsStarted { get; set; }

        /// <summary>The number of minutes a site export should be considered valid based on its last-updated date before it's ignored.</summary>
        private static int ExportStaleAge => (BackgroundService.UpdateCheckConfig?.Value.SuccessCacheMinutes ?? 0) + 10;


        /*********
        ** Public methods
        *********/
        /****
        ** Hosted service
        ****/
        /// <summary>Construct an instance.</summary>
        /// <param name="wikiCache">The cache in which to store wiki metadata.</param>
        /// <param name="modCache">The cache in which to store mod data.</param>
        /// <param name="curseForgeExportCache">The cache in which to store mod data from the CurseForge export API.</param>
        /// <param name="curseForgeExportApiClient">The HTTP client for fetching the mod export from the CurseForge export API.</param>
        /// <param name="nexusExportCache">The cache in which to store mod data from the Nexus export API.</param>
        /// <param name="nexusExportApiClient">The HTTP client for fetching the mod export from the Nexus Mods export API.</param>
        /// <param name="hangfireStorage">The Hangfire storage implementation.</param>
        /// <param name="updateCheckConfig">The config settings for mod update checks.</param>
        [SuppressMessage("ReSharper", "UnusedParameter.Local", Justification = "The Hangfire reference forces it to initialize first, since it's needed by the background service.")]
        public BackgroundService(IWikiCacheRepository wikiCache, IModCacheRepository modCache, ICurseForgeExportCacheRepository curseForgeExportCache, ICurseForgeExportApiClient curseForgeExportApiClient, INexusExportCacheRepository nexusExportCache, INexusExportApiClient nexusExportApiClient, JobStorage hangfireStorage, IOptions<ModUpdateCheckConfig> updateCheckConfig)
        {
            BackgroundService.WikiCache = wikiCache;
            BackgroundService.ModCache = modCache;
            BackgroundService.CurseForgeExportApiClient = curseForgeExportApiClient;
            BackgroundService.CurseForgeExportCache = curseForgeExportCache;
            BackgroundService.NexusExportCache = nexusExportCache;
            BackgroundService.NexusExportApiClient = nexusExportApiClient;
            BackgroundService.UpdateCheckConfig = updateCheckConfig;

            _ = hangfireStorage; // parameter is only received to initialize it before the background service
        }

        /// <summary>Start the service.</summary>
        /// <param name="cancellationToken">Tracks whether the start process has been aborted.</param>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            this.TryInit();

            bool enableCurseForgeExport = BackgroundService.CurseForgeExportApiClient is not DisabledCurseForgeExportApiClient;
            bool enableNexusExport = BackgroundService.NexusExportApiClient is not DisabledNexusExportApiClient;

            // set startup tasks
            BackgroundJob.Enqueue(() => BackgroundService.UpdateWikiAsync(null));
            if (enableCurseForgeExport)
                BackgroundJob.Enqueue(() => BackgroundService.UpdateCurseForgeExportAsync(null));
            if (enableNexusExport)
                BackgroundJob.Enqueue(() => BackgroundService.UpdateNexusExportAsync(null));
            BackgroundJob.Enqueue(() => BackgroundService.RemoveStaleModsAsync());

            // set recurring tasks
            RecurringJob.AddOrUpdate("update wiki data", () => BackgroundService.UpdateWikiAsync(null), "*/10 * * * *");      // every 10 minutes
            if (enableCurseForgeExport)
                RecurringJob.AddOrUpdate("update CurseForge export", () => BackgroundService.UpdateCurseForgeExportAsync(null), "*/10 * * * *");
            if (enableNexusExport)
                RecurringJob.AddOrUpdate("update Nexus export", () => BackgroundService.UpdateNexusExportAsync(null), "*/10 * * * *");
            RecurringJob.AddOrUpdate("remove stale mods", () => BackgroundService.RemoveStaleModsAsync(), "2/10 * * * *"); // offset by 2 minutes so it runs after updates (e.g. 00:02, 00:12, etc)

            BackgroundService.IsStarted = true;

            return Task.CompletedTask;
        }

        /// <summary>Triggered when the application host is performing a graceful shutdown.</summary>
        /// <param name="cancellationToken">Tracks whether the shutdown process should no longer be graceful.</param>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            BackgroundService.IsStarted = false;

            if (BackgroundService.JobServer != null)
                await BackgroundService.JobServer.WaitForShutdownAsync(cancellationToken);
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        public void Dispose()
        {
            BackgroundService.IsStarted = false;

            BackgroundService.JobServer?.Dispose();
        }

        /****
        ** Tasks
        ****/
        /// <summary>Update the cached wiki metadata.</summary>
        /// <param name="context">Information about the context in which the job is performed. This is injected automatically by Hangfire.</param>
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
        public static async Task UpdateWikiAsync(PerformContext? context)
        {
            if (!BackgroundService.IsStarted)
                throw new InvalidOperationException($"Must call {nameof(BackgroundService.StartAsync)} before scheduling tasks.");

            context.WriteLine("Fetching data from wiki...");
            WikiModList wikiCompatList = await new ModToolkit().GetWikiCompatibilityListAsync();

            context.WriteLine("Saving data...");
            BackgroundService.WikiCache.SaveWikiData(wikiCompatList.StableVersion, wikiCompatList.BetaVersion, wikiCompatList.Mods);

            context.WriteLine("Done!");
        }

        /// <summary>Update the cached CurseForge mod dump.</summary>
        /// <param name="context">Information about the context in which the job is performed. This is injected automatically by Hangfire.</param>
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
        public static async Task UpdateCurseForgeExportAsync(PerformContext? context)
        {
            await UpdateExportAsync(
                context,
                BackgroundService.CurseForgeExportCache!,
                BackgroundService.CurseForgeExportApiClient!,
                client => client.FetchLastModifiedDateAsync(),
                async (cache, client) => cache.SetData(await client.FetchExportAsync())
            );
        }

        /// <summary>Update the cached Nexus mod dump.</summary>
        /// <param name="context">Information about the context in which the job is performed. This is injected automatically by Hangfire.</param>
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
        public static async Task UpdateNexusExportAsync(PerformContext? context)
        {
            await UpdateExportAsync(
                context,
                BackgroundService.NexusExportCache!,
                BackgroundService.NexusExportApiClient!,
                client => client.FetchLastModifiedDateAsync(),
                async (cache, client) => cache.SetData(await client.FetchExportAsync())
            );
        }

        /// <summary>Remove mods which haven't been requested in over 48 hours.</summary>
        public static Task RemoveStaleModsAsync()
        {
            if (!BackgroundService.IsStarted)
                throw new InvalidOperationException($"Must call {nameof(BackgroundService.StartAsync)} before scheduling tasks.");

            // remove mods in mod cache
            BackgroundService.ModCache.RemoveStaleMods(TimeSpan.FromHours(48));

            return Task.CompletedTask;
        }


        /*********
        ** Private method
        *********/
        /// <summary>Initialize the background service if it's not already initialized.</summary>
        /// <exception cref="InvalidOperationException">The background service is already initialized.</exception>
        private void TryInit()
        {
            if (BackgroundService.JobServer != null)
                throw new InvalidOperationException("The scheduler service is already started.");

            BackgroundService.JobServer = new BackgroundJobServer();
        }

        /// <summary>Update the cached mods export for a site.</summary>
        /// <typeparam name="TCacheRepository">The export cache repository type.</typeparam>
        /// <typeparam name="TExportApiClient">The export API client.</typeparam>
        /// <param name="context">Information about the context in which the job is performed. This is injected automatically by Hangfire.</param>
        /// <param name="cache">The export cache to update.</param>
        /// <param name="client">The export API with which to fetch data from the remote API.</param>
        /// <param name="fetchLastModifiedDateAsync">Fetch the date when the export on the server was last modified.</param>
        /// <param name="fetchDataAsync">Fetch the latest export file from the Nexus Mods export API.</param>
        /// <exception cref="InvalidOperationException">The <see cref="StartAsync"/> method wasn't called before running this task.</exception>
        private static async Task UpdateExportAsync<TCacheRepository, TExportApiClient>(PerformContext? context, TCacheRepository cache, TExportApiClient client, Func<TExportApiClient, Task<DateTimeOffset>> fetchLastModifiedDateAsync, Func<TCacheRepository, TExportApiClient, Task> fetchDataAsync)
            where TCacheRepository : IExportCacheRepository
        {
            if (!BackgroundService.IsStarted)
                throw new InvalidOperationException($"Must call {nameof(BackgroundService.StartAsync)} before scheduling tasks.");

            // refresh data
            context.WriteLine("Checking if we can refresh the data...");
            if (BackgroundService.CanRefreshFromExportApi(await fetchLastModifiedDateAsync(client), cache, out string? failReason))
            {
                context.WriteLine("Fetching data...");
                await fetchDataAsync(cache, client);
                context.WriteLine($"Cache updated. The data was last modified {BackgroundService.FormatDateModified(cache.GetLastModified())}.");
            }
            else
                context.WriteLine($"Skipped data fetch: {failReason}.");

            // clear if stale
            if (cache.IsStale(BackgroundService.ExportStaleAge))
            {
                context.WriteLine("The cached data is stale, clearing cache...");
                cache.Clear();
            }

            context.WriteLine("Done!");
        }

        /// <summary>Get whether newer non-stale data can be fetched from the server.</summary>
        /// <param name="serverModified">The last-modified data from the remote API.</param>
        /// <param name="repository">The repository to update.</param>
        /// <param name="failReason">The reason to log if we can't fetch data.</param>
        private static bool CanRefreshFromExportApi(DateTimeOffset serverModified, IExportCacheRepository repository, [NotNullWhen(false)] out string? failReason)
        {
            if (repository.IsStale(serverModified, BackgroundService.ExportStaleAge))
            {
                failReason = $"server was last modified {BackgroundService.FormatDateModified(serverModified)}, which exceeds the {BackgroundService.ExportStaleAge}-minute-stale limit";
                return false;
            }

            if (repository.IsLoaded())
            {
                DateTimeOffset localModified = repository.GetLastModified();
                if (localModified >= serverModified)
                {
                    failReason = $"server was last modified {BackgroundService.FormatDateModified(serverModified)}, which {(serverModified == localModified ? "matches our cached data" : $"is older than our cached {BackgroundService.FormatDateModified(localModified)}")}";
                    return false;
                }
            }

            failReason = null;
            return true;
        }

        /// <summary>Format a 'date modified' value for the task logs.</summary>
        /// <param name="date">The date to log.</param>
        private static string FormatDateModified(DateTimeOffset date)
        {
            return $"{date:O} (age: {(DateTimeOffset.UtcNow - date).Humanize()})";
        }
    }
}
