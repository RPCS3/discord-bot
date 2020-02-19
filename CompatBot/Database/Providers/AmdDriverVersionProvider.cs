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
            if (await SyncObj.WaitAsync(0).ConfigureAwait(false))
                try
                {
                    using (var httpClient = HttpClientFactory.Create(new CompressionMessageHandler()))
                    {
                        using var response = await httpClient.GetStreamAsync("https://raw.githubusercontent.com/GPUOpen-Drivers/amd-vulkan-versions/master/amdversions.xml").ConfigureAwait(false);
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
                    Config.Log.Warn(e, "Failed to update AMD version mapping");
                }
                finally
                {
                    SyncObj.Release();
                }
        }

        public static async Task<string> GetFromOpenglAsync(string openglVersion, bool autoRefresh = true)
        {
            if (OpenglToDriver.TryGetValue(openglVersion, out var result) && result != null)
                return result;

            if (Version.TryParse(openglVersion, out var glVersion))
            {
                var glVersions = new List<(Version v, string vv)>(OpenglToDriver.Count);
                foreach (var key in OpenglToDriver.Keys)
                {
                    if (Version.TryParse(key, out var ver))
                        glVersions.Add((ver, OpenglToDriver[key]));
                }
                if (glVersions.Count == 0)
                    return openglVersion;

                glVersions.Sort((l, r) => l.v < r.v ? -1 : l.v > r.v ? 1 : 0);
                if (glVersion < glVersions[0].v)
                    return $"older than {glVersions[0].vv} ({openglVersion})";

                var newest = glVersions.Last();
                if (glVersion > newest.v)
                {
                    if (autoRefresh)
                    {
                        await RefreshAsync().ConfigureAwait(false);
                        return await GetFromOpenglAsync(openglVersion, false).ConfigureAwait(false);
                    }

                    return $"newer than {newest.vv} ({openglVersion})";
                }

                var approximate = glVersions.FirstOrDefault(v => v.v.Minor == glVersion.Minor && v.v.Build == glVersion.Build);
                if (!string.IsNullOrEmpty(approximate.vv))
                    return $"{approximate.vv} rev {glVersion.Revision}";

                if (string.IsNullOrEmpty(approximate.vv))
                    for (var i = 0; i < glVersions.Count - 1; i++)
                        if (glVersion > glVersions[i].v && glVersion < glVersions[i + 1].v)
                        {
                            approximate = glVersions[i];
                            break;
                        }
                if (!string.IsNullOrEmpty(approximate.vv))
                    return $"probably {approximate.vv}";
            }

            return openglVersion;
        }

        public static async Task<string> GetFromVulkanAsync(string vulkanVersion, bool autoRefresh = true)
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
                    return $"older than {vkVersions[0].vv} ({vulkanVersion})";

                var newest = vkVersions.Last();
                if (vkVer > newest.v)
                {
                    if (autoRefresh)
                    {
                        await RefreshAsync().ConfigureAwait(false);
                        return await GetFromVulkanAsync(vulkanVersion, false).ConfigureAwait(false);
                    }

                    return $"newer than {newest.vv} ({vulkanVersion})";
                }
                else
                {
                    for (var i = 1; i < vkVersions.Count; i++)
                    {
                        if (vkVer < vkVersions[i].v)
                        {
                            var lowerVer = vkVersions[i - 1].v;
                            var mapKey = VulkanToDriver.Keys.FirstOrDefault(k => Version.Parse(k) == lowerVer);
                            if (mapKey != null)
                            {
                                if (VulkanToDriver.TryGetValue(mapKey, out var driverList))
                                {
                                    var oldestLowerVersion = driverList.Select(Version.Parse).OrderByDescending(v => v).First();
                                    return $"unknown version between {oldestLowerVersion} and {vkVersions[i].vv} ({vulkanVersion})";
                                }
                            }
                        }
                    }
                }
            }

            return vulkanVersion;
        }
    }
}
