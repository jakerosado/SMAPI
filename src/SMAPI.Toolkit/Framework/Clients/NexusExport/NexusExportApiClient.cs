using System;
using System.Net.Http;
using System.Threading.Tasks;
using Pathoschild.Http.Client;
using StardewModdingAPI.Toolkit.Framework.Clients.NexusExport.ResponseModels;

namespace StardewModdingAPI.Toolkit.Framework.Clients.NexusExport
{
    /// <inheritdoc cref="INexusExportApiClient" />
    public class NexusExportApiClient : INexusExportApiClient
    {
        /*********
        ** Fields
        *********/
        /// <summary>The underlying HTTP client.</summary>
        private readonly IClient Client;


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="userAgent">The user agent for the Nexus export API.</param>
        /// <param name="baseUrl">The base URL for the Nexus export API.</param>
        public NexusExportApiClient(string userAgent, string baseUrl)
        {
            this.Client = new FluentClient(baseUrl).SetUserAgent(userAgent);
        }

        /// <inheritdoc />
        public async Task<DateTimeOffset> FetchLastModifiedDateAsync()
        {
            IResponse response = await this.Client.SendAsync(HttpMethod.Head, "");

            return this.ReadLastModified(response);
        }

        /// <inheritdoc />
        public async Task<NexusFullExport> FetchExportAsync()
        {
            IResponse response = await this.Client.GetAsync("");

            NexusFullExport export = await response.As<NexusFullExport>();
            export.LastUpdated = this.ReadLastModified(response);

            return export;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.Client.Dispose();
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Read the <c>Last-Modified</c> header from an API response.</summary>
        /// <param name="response">The response from the Nexus API.</param>
        /// <exception cref="InvalidOperationException">The response doesn't include the required <c>Last-Modified</c> header.</exception>
        private DateTimeOffset ReadLastModified(IResponse response)
        {
            return response.Message.Content.Headers.LastModified ?? throw new InvalidOperationException("Can't fetch from Nexus export API: expected Last-Modified header wasn't set.");
        }
    }
}
