using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CompatApiClient.Compression;

namespace CompatBot.Database.Providers
{
    internal static class AmdDriverVersionProvider
    {
        private static readonly Dictionary<string, List<string>> VulkanToDriver = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, string> OpenglToDriver = new Dictionary<string, string>();
        private static readonly SemaphoreSlim SyncObj = new SemaphoreSlim(1, 1);

        public static async Task RefreshAsync()
        {
            if (SyncObj.Wait(0))
                try
                {
                    using (var httpClient = HttpClientFactory.Create(new CompressionMessageHandler()))
                    using (var response = await httpClient.GetStreamAsync("https://raw.githubusercontent.com/GPUOpen-Drivers/amd-vulkan-versions/master/amdversions.xml").ConfigureAwait(false))
                    {
                        var xml = await XDocument.LoadAsync(response, LoadOptions.None, Config.Cts.Token).ConfigureAwait(false);
                        foreach (var driver in xml.Root.Elements("driver"))
                        {
                            var winVer = (string)driver.Element("windows-version");
                            var vkVer = (string)driver.Element("vulkan-version");
                            var driverVer = (string)driver.Attribute("version");
                            if (!VulkanToDriver.TryGetValue(vkVer, out var verList))
                                VulkanToDriver[vkVer] = (verList = new List<string>());
                            verList.Insert(0, driverVer);
                            OpenglToDriver[winVer] = driverVer;
                        }
                    }
                    foreach (var key in VulkanToDriver.Keys.ToList())
                        VulkanToDriver[key] = VulkanToDriver[key].Distinct().ToList();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
                finally
                {
                    SyncObj.Release();
                }
        }

        public static string GetFromOpengl(string openglVersion)
        {
            if (OpenglToDriver.TryGetValue(openglVersion, out var result) && result != null)
                return result;
            return openglVersion;
        }

        public static async Task<string> GetFromVulkanAsync(string vulkanVersion)
        {
            if (!VulkanToDriver.TryGetValue(vulkanVersion, out var result))
                await RefreshAsync().ConfigureAwait(false);

            if (result?.Count > 0 || (VulkanToDriver.TryGetValue(vulkanVersion, out result) && result.Count > 0))
            {
                if (result.Count == 1)
                    return result[0];
                return $"{result.First()} - {result.Last()}";
            }

            if (Version.TryParse(vulkanVersion, out var vkVer))
            {
                var vkVersions = new List<(Version v, string vv)>(VulkanToDriver.Count);
                foreach (var key in VulkanToDriver.Keys)
                {
                    if (Version.TryParse(key, out var ver))
                        vkVersions.Add((ver, VulkanToDriver[key].First()));
                }
                if (vkVersions.Count == 0)
                    return vulkanVersion;

                vkVersions.Sort((l, r) => l.v < r.v ? -1 : l.v > r.v ? 1 : 0);
                if (vkVer < vkVersions[0].v)
                    return $"older than {vkVersions[0].vv}";

                var newest = vkVersions.Last();
                if (vkVer > newest.v)
                    return $"newer than {newest.vv}";
            }

            return vulkanVersion;
        }
    }
}
