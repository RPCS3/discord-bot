using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using CompatApiClient.Compression;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.ThumbScrapper
{
    internal static class GameTdbScraper
    {
        private static readonly HttpClient HttpClient = HttpClientFactory.Create(new CompressionMessageHandler());
        private static readonly Uri TitleDownloadLink = new Uri("https://www.gametdb.com/ps3tdb.zip?LANG=EN");
        private static readonly Regex CoverArtLink = new Regex(@"(?<cover_link>https?://art\.gametdb\.com/ps3/cover(?!full)[/\w\d]+\.jpg(\?\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.ExplicitCapture);
        private static readonly List<string> PreferredOrder = new List<string>{"coverHQ", "coverM", "cover"};

        public static async Task RunAsync(CancellationToken cancellationToken)
        {
            do
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await UpdateGameTitlesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    PrintError(e);
                }
                await Task.Delay(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
            } while (!cancellationToken.IsCancellationRequested);
        }

        public static async Task<string?> GetThumbAsync(string productCode)
        {
            try
            {
                var html = await HttpClient.GetStringAsync("https://www.gametdb.com/PS3/" + productCode).ConfigureAwait(false);
                var coverLinks = CoverArtLink.Matches(html).Select(m => m.Groups["cover_link"].Value).Distinct().Where(l => l.Contains(productCode, StringComparison.InvariantCultureIgnoreCase)).ToList();
                return coverLinks.FirstOrDefault(l => l.Contains("coverHQ", StringComparison.InvariantCultureIgnoreCase)) ??
                       coverLinks.FirstOrDefault(l => l.Contains("coverM", StringComparison.InvariantCultureIgnoreCase)) ??
                       coverLinks.FirstOrDefault();
            }
            catch (Exception e)
            {
                if (e is HttpRequestException hre && hre.Message.Contains("404"))
                    return null;

                PrintError(e);
            }
            return null;
        }

        private static async Task UpdateGameTitlesAsync(CancellationToken cancellationToken)
        {
            var container = Path.GetFileName(TitleDownloadLink.AbsolutePath);
            try
            {
                if (ScrapeStateProvider.IsFresh(container))
                    return;

                Config.Log.Debug("Scraping GameTDB for game titles...");
                await using var fileStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 16384, FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose);
                await using (var downloadStream = await HttpClient.GetStreamAsync(TitleDownloadLink, cancellationToken).ConfigureAwait(false))
                    await downloadStream.CopyToAsync(fileStream, 16384, cancellationToken).ConfigureAwait(false);
                fileStream.Seek(0, SeekOrigin.Begin);
                using var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read);
                var logEntry = zipArchive.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.InvariantCultureIgnoreCase));
                if (logEntry == null)
                    throw new InvalidOperationException("No zip entries that match the .xml criteria");

                await using var zipStream = logEntry.Open();
                using var xmlReader = XmlReader.Create(zipStream, new XmlReaderSettings { Async = true });
                xmlReader.ReadToFollowing("PS3TDB");
                var version = xmlReader.GetAttribute("version");
                if (!DateTime.TryParseExact(version, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
                    return;

                if (ScrapeStateProvider.IsFresh("PS3TDB", timestamp))
                {
                    await ScrapeStateProvider.SetLastRunTimestampAsync("PS3TDB").ConfigureAwait(false);
                    return;
                }

                while (!cancellationToken.IsCancellationRequested && xmlReader.ReadToFollowing("game"))
                {
                    if (!xmlReader.ReadToFollowing("id"))
                        continue;
                    
                    var productId = (await xmlReader.ReadElementContentAsStringAsync().ConfigureAwait(false)).ToUpperInvariant();
                    if (!ProductCodeLookup.ProductCode.IsMatch(productId))
                        continue;

                    string? title = null;
                    if (xmlReader.ReadToFollowing("locale") && xmlReader.ReadToFollowing("title"))
                        title = await xmlReader.ReadElementContentAsStringAsync().ConfigureAwait(false);

                    if (string.IsNullOrEmpty(title))
                        continue;

                    await using var db = new ThumbnailDb();
                    var item = await db.Thumbnail.FirstOrDefaultAsync(t => t.ProductCode == productId, cancellationToken).ConfigureAwait(false);
                    if (item is null)
                    {
                        await db.Thumbnail.AddAsync(new Thumbnail {ProductCode = productId, Name = title}, cancellationToken).ConfigureAwait(false);
                        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else if (item.Name != title && item.Timestamp == 0)
                    {
                        item.Name = title;
                        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                await ScrapeStateProvider.SetLastRunTimestampAsync("PS3TDB").ConfigureAwait(false);
                await ScrapeStateProvider.SetLastRunTimestampAsync(container).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                PrintError(e);
            }
            finally
            {
                Config.Log.Debug("Finished scraping GameTDB for game titles");
            }
        }

        private static void PrintError(Exception e)
        {
            Config.Log.Error(e, "Error scraping titles from GameTDB");
        }
    }
}
