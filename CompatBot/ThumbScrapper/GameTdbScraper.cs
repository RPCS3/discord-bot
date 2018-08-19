using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
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
                await Task.Delay(TimeSpan.FromDays(30), cancellationToken).ConfigureAwait(false);
            } while (!cancellationToken.IsCancellationRequested);
        }

        private static async Task UpdateGameTitlesAsync(CancellationToken cancellationToken)
        {
            var container = Path.GetFileName(TitleDownloadLink.AbsolutePath);
            try
            {
                if (ScrapeStateProvider.IsFresh(container))
                    return;

                Console.WriteLine("Scraping GameTDB for game titles...");
                using (var fileStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 16384, FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose))
                {
                    using (var downloadStream = await HttpClient.GetStreamAsync(TitleDownloadLink).ConfigureAwait(false))
                        await downloadStream.CopyToAsync(fileStream, 16384, cancellationToken).ConfigureAwait(false);
                    fileStream.Seek(0, SeekOrigin.Begin);
                    using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                    {
                        var logEntry = zipArchive.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.InvariantCultureIgnoreCase));
                        if (logEntry == null)
                            throw new InvalidOperationException("No zip entries that match the .xml criteria");

                        using (var zipStream = logEntry.Open())
                        using (var xmlReader = XmlReader.Create(zipStream))
                        {
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
                                if (xmlReader.ReadToFollowing("id"))
                                {
                                    var productId = xmlReader.ReadElementContentAsString().ToUpperInvariant();
                                    if (!ProductCodeLookup.ProductCode.IsMatch(productId))
                                        continue;

                                    string title = null;
                                    if (xmlReader.ReadToFollowing("locale") && xmlReader.ReadToFollowing("title"))
                                        title = xmlReader.ReadElementContentAsString();

                                    if (!string.IsNullOrEmpty(title))
                                    {
                                        using (var db = new ThumbnailDb())
                                        {
                                            var item = await db.Thumbnail.FirstOrDefaultAsync(t => t.ProductCode == productId, cancellationToken).ConfigureAwait(false);
                                            if (item == null)
                                            {
                                                await db.Thumbnail.AddAsync(new Thumbnail {ProductCode = productId, Name = title}, cancellationToken).ConfigureAwait(false);
                                                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                                            }
                                            else
                                            {
                                                if (item.Name != title && item.Timestamp == 0)
                                                {
                                                    item.Name = title;
                                                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            await ScrapeStateProvider.SetLastRunTimestampAsync("PS3TDB").ConfigureAwait(false);
                        }

                    }
                }
                await ScrapeStateProvider.SetLastRunTimestampAsync(container).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                PrintError(e);
            }
            finally
            {
                Console.WriteLine("Finished scraping GameTDB for game titles");
            }
        }

        private static void PrintError(Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error scraping titles from GameTDB: " + e);
            Console.ResetColor();
        }
    }
}
