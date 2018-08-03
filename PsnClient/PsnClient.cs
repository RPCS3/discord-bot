using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using Newtonsoft.Json;
using PsnClient.POCOs;
using PsnClient.Utils;
using JsonContractResolver = CompatApiClient.JsonContractResolver;

namespace PsnClient
{
    public class Client
    {
        private readonly HttpClient client;
        private readonly MediaTypeFormatterCollection dashedFormatters;
        private readonly MediaTypeFormatterCollection underscoreFormatters;
        private static readonly Regex ContainerIdLink = new Regex(@"(?<id>STORE-(\w|\d)+-(\w|\d)+)");

        public Client()
        {
            client = HttpClientFactory.Create(new CompressionMessageHandler());
            var dashedSettings = new JsonSerializerSettings
            {
                ContractResolver = new JsonContractResolver(NamingStyles.Dashed),
                NullValueHandling = NullValueHandling.Ignore
            };
            dashedFormatters = new MediaTypeFormatterCollection(new[] { new JsonMediaTypeFormatter { SerializerSettings = dashedSettings } });

            var underscoreSettings = new JsonSerializerSettings
            {
                ContractResolver = new JsonContractResolver(NamingStyles.Underscore),
                NullValueHandling = NullValueHandling.Ignore
            };
            underscoreFormatters = new MediaTypeFormatterCollection(new[] { new JsonMediaTypeFormatter { SerializerSettings = underscoreSettings } });
        }

        public async Task<AppLocales> GetLocales(CancellationToken cancellationToken)
        {
            try
            {
                HttpResponseMessage response;
                using (var message = new HttpRequestMessage(HttpMethod.Get, "https://transact.playstation.com/assets/app.json"))
                using (response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                    try
                    {
                        await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                        var items = await response.Content.ReadAsAsync<AppLocales[]>(underscoreFormatters, cancellationToken).ConfigureAwait(false);
                        return items.FirstOrDefault(i => i?.EnabledLocales != null);
                    }
                    catch (Exception e)
                    {
                        ConsoleLogger.PrintError(e, response);
                        return null;
                    }
            }
            catch (Exception e)
            {
                ConsoleLogger.PrintError(e, null);
                return null;
            }
        }

        public async Task<Stores> GetStoresAsync(string locale, CancellationToken cancellationToken)
        {
            try
            {
                var cookieHeaderValue = await GetSessionCookies(locale, cancellationToken).ConfigureAwait(false);
                using (var getMessage = new HttpRequestMessage(HttpMethod.Get, "https://store.playstation.com/kamaji/api/valkyrie_storefront/00_09_000/user/stores"))
                {
                    getMessage.Headers.Add("Cookie", cookieHeaderValue);
                    HttpResponseMessage response;
                    using (response = await client.SendAsync(getMessage, cancellationToken).ConfigureAwait(false))
                        try
                        {
                            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                            return await response.Content.ReadAsAsync<Stores>(underscoreFormatters, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            ConsoleLogger.PrintError(e, response);
                            return null;
                        }
                }
            }
            catch (Exception e)
            {
                ConsoleLogger.PrintError(e, null);
                return null;
            }
        }

        public async Task<List<string>> GetMainPageNavigationContainerIdsAsync(string locale, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = null;
            try
            {
                var baseUrl = $"https://store.playstation.com/{locale}/";
                var sessionCookies = await GetSessionCookies(locale, cancellationToken).ConfigureAwait(false);
                using (var message = new HttpRequestMessage(HttpMethod.Get, baseUrl))
                {
                    message.Headers.Add("Cookie", sessionCookies);
                    response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);

                    await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                    var tries = 0;
                    while (response.StatusCode == HttpStatusCode.Redirect && tries < 10 && !cancellationToken.IsCancellationRequested)
                    {
                        using (var newLocationMessage = new HttpRequestMessage(HttpMethod.Get, response.Headers.Location))
                        {
                            newLocationMessage.Headers.Add("Cookie", sessionCookies);
                            var redirectResponse = await client.SendAsync(newLocationMessage, cancellationToken).ConfigureAwait(false);
                            response.Dispose();
                            response = redirectResponse;
                        }
                        tries++;
                    }
                    if (response.StatusCode == HttpStatusCode.Redirect)
                        return new List<string>(0);
                }

                using (response)
                    try
                    {
                        await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                        var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var matches = ContainerIdLink.Matches(html);
                        var result = new List<string>();
                        foreach (Match m in matches)
                            if (m.Groups["id"].Value is string id && !string.IsNullOrEmpty(id))
                                result.Add(id);
                        return result;
                    }
                    catch (Exception e)
                    {
                        ConsoleLogger.PrintError(e, response);
                        return null;
                    }
            }
            catch (Exception e)
            {
                ConsoleLogger.PrintError(e, response);
                return null;
            }
        }

        public async Task<StoreNavigation> GetStoreNavigationAsync(string locale, string containerId, CancellationToken cancellationToken)
        {
            try
            {
                var loc = locale.AsLocaleData();
                var baseUrl = $"https://store.playstation.com/valkyrie-api/{loc.language}/{loc.country}/999/storefront/{containerId}";
                HttpResponseMessage response;
                using (var message = new HttpRequestMessage(HttpMethod.Get, baseUrl))
                using (response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false))
                    try
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound)
                            return null;

                        await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                        return await response.Content.ReadAsAsync<StoreNavigation>(dashedFormatters, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        ConsoleLogger.PrintError(e, response);
                        return null;
                    }
            }
            catch (Exception e)
            {
                ConsoleLogger.PrintError(e, null);
                return null;
            }
        }

        public async Task<Container> GetGameContainerAsync(string locale, string containerId, int start, int take, Dictionary<string, string> filters, CancellationToken cancellationToken)
        {
            try
            {
                var loc = locale.AsLocaleData();
                var url = new Uri($"https://store.playstation.com/valkyrie-api/{loc.language}/{loc.country}/999/container/{containerId}");
                filters = filters ?? new Dictionary<string, string>();
                filters["start"] = start.ToString();
                filters["size"] = take.ToString();
                filters["bucket"] = "games";
                url = url.SetQueryParameters(filters);
                HttpResponseMessage response;
                using (var message = new HttpRequestMessage(HttpMethod.Get, url))
                using (response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false))
                    try
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound)
                            return null;

                        await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                        return await response.Content.ReadAsAsync<Container>(dashedFormatters, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        ConsoleLogger.PrintError(e, response);
                        return null;
                    }
            }
            catch (Exception e)
            {
                ConsoleLogger.PrintError(e, null);
                return null;
            }
        }

        public async Task<Container> ResolveContentAsync(string locale, string contentId, int depth, CancellationToken cancellationToken)
        {
            try
            {
                var loc = locale.AsLocaleData();
                HttpResponseMessage response;
                using (var message = new HttpRequestMessage(HttpMethod.Get, $"https://store.playstation.com/valkyrie-api/{loc.language}/{loc.country}/999/resolve/{contentId}?depth={depth}"))
                using (response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false))
                    try
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound)
                            return null;

                        await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                        return await response.Content.ReadAsAsync<Container>(dashedFormatters, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        ConsoleLogger.PrintError(e, response);
                        return null;
                    }
            }
            catch (Exception e)
            {
                ConsoleLogger.PrintError(e, null);
                return null;
            }
        }

        private async Task<string> GetSessionCookies(string locale, CancellationToken cancellationToken)
        {
            var loc = locale.AsLocaleData();
            var uri = new Uri("https://store.playstation.com/kamaji/api/valkyrie_storefront/00_09_000/user/session");
            var tries = 0;
            do
            {
                try
                {
                    HttpResponseMessage response;
                    using (var deleteMessage = new HttpRequestMessage(HttpMethod.Delete, uri))
                    using (response = await client.SendAsync(deleteMessage, cancellationToken))
                        if (response.StatusCode != HttpStatusCode.OK)
                            ConsoleLogger.PrintError(new InvalidOperationException("Couldn't delete current session"), response, ConsoleColor.Yellow);

                    var authMessage = new HttpRequestMessage(HttpMethod.Post, uri)
                    {
                        Content = new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            ["country_code"] = loc.country,
                            ["language_code"] = loc.language,
                        })
                    };
                    using (authMessage)
                    using (response = await client.SendAsync(authMessage, cancellationToken).ConfigureAwait(false))
                        try
                        {
                            var cookieContainer = new CookieContainer();
                            foreach (var cookie in response.Headers.GetValues("set-cookie"))
                                cookieContainer.SetCookies(uri, cookie);
                            return cookieContainer.GetCookieHeader(uri);
                        }
                        catch (Exception e)
                        {
                            ConsoleLogger.PrintError(e, response, tries < 3 ? ConsoleColor.Yellow : ConsoleColor.Red);
                            tries++;
                        }
                }
                catch (Exception e)
                {
                    ConsoleLogger.PrintError(e, null, tries < 3 ? ConsoleColor.Yellow : ConsoleColor.Red);
                    tries++;
                }
            } while (tries < 3);
            throw new InvalidOperationException("Couldn't obtain web session");
        }

    }
}
