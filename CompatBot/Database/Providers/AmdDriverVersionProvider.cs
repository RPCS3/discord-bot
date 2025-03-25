using System.Net.Http;
using System.Xml.Linq;
using CompatApiClient.Compression;

namespace CompatBot.Database.Providers;

internal static class AmdDriverVersionProvider
{
    private static readonly Dictionary<string, List<string>> VulkanToDriver = new();
    private static readonly Dictionary<string, string> OpenglToDriver = new();
    private static readonly Dictionary<string, string> InternalToDriver = new();
    private static readonly SemaphoreSlim SyncObj = new(1, 1);

    public static async Task RefreshAsync()
    {
        if (await SyncObj.WaitAsync(0).ConfigureAwait(false))
            try
            {
                using var httpClient = HttpClientFactory.Create(new CompressionMessageHandler());
                await using var response = await httpClient.GetStreamAsync("https://raw.githubusercontent.com/GPUOpen-Drivers/amd-vulkan-versions/master/amdversions.xml").ConfigureAwait(false);
                var xml = await XDocument.LoadAsync(response, LoadOptions.None, Config.Cts.Token).ConfigureAwait(false);
                if (xml.Root is null)
                {
                    Config.Log.Warn("Failed to update AMD version mapping");
                    return;
                }

                foreach (var driver in xml.Root.Elements("driver"))
                {
                    var winVer = (string?)driver.Element("windows-version");
                    var vkVer = (string?)driver.Element("vulkan-version");
                    var internVer = (string?)driver.Element("internal-version");
                    var driverVer = (string?)driver.Attribute("version");
                    if (vkVer is null)
                        continue;

                    if (!VulkanToDriver.TryGetValue(vkVer, out var verList))
                        VulkanToDriver[vkVer] = verList = new();
                    if (string.IsNullOrEmpty(driverVer))
                        continue;
                        
                    verList.Insert(0, driverVer);
                    if (!string.IsNullOrEmpty(winVer))
                        OpenglToDriver[winVer] = driverVer;
                    if (!string.IsNullOrEmpty(internVer))
                        InternalToDriver[internVer] = driverVer;
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
        if (OpenglToDriver.TryGetValue(openglVersion, out var result))
            return result;

        if (!Version.TryParse(openglVersion, out var glVersion))
            return openglVersion;

        if (glVersion is { Major: >= 22, Minor: < 13, Build: <10, Revision: > 220600 })
            return $"{glVersion.Major}.{glVersion.Minor}.{glVersion.Build}";
            
        var glVersions = new List<(Version glVer, string driverVer)>(OpenglToDriver.Count);
        foreach (var key in OpenglToDriver.Keys)
        {
            if (Version.TryParse(key, out var ver))
                glVersions.Add((ver, OpenglToDriver[key]));
        }
        if (glVersions.Count == 0)
            return openglVersion;

        glVersions.Sort((l, r) => l.glVer < r.glVer ? -1 : l.glVer > r.glVer ? 1 : 0);
        if (glVersion < glVersions[0].glVer)
            return $"older than {glVersions[0].driverVer} ({openglVersion})";

        var newest = glVersions.Last();
        if (glVersion > newest.glVer)
        {
            if (autoRefresh)
            {
                await RefreshAsync().ConfigureAwait(false);
                return await GetFromOpenglAsync(openglVersion, false).ConfigureAwait(false);
            }

            return $"newer than {newest.driverVer} ({openglVersion})";
        }

        var approximate = glVersions.FirstOrDefault(v => v.glVer.Minor == glVersion.Minor && v.glVer.Build == glVersion.Build);
        if (!string.IsNullOrEmpty(approximate.driverVer))
            return $"{approximate.driverVer} rev {glVersion.Revision}";

        if (string.IsNullOrEmpty(approximate.driverVer))
            for (var i = 0; i < glVersions.Count - 1; i++)
                if (glVersion > glVersions[i].glVer && glVersion < glVersions[i + 1].glVer)
                {
                    approximate = glVersions[i];
                    break;
                }
        if (!string.IsNullOrEmpty(approximate.driverVer))
            return $"probably {approximate.driverVer}";

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
            var vkVersions = new List<(Version vkVer, List<string> driverVers)>(VulkanToDriver.Count);
            foreach (var key in VulkanToDriver.Keys)
            {
                if (Version.TryParse(key, out var ver))
                    vkVersions.Add((ver, VulkanToDriver[key]));
            }
            if (vkVersions.Count == 0)
                return vulkanVersion;

            vkVersions.Sort((l, r) => l.vkVer < r.vkVer ? -1 : l.vkVer > r.vkVer ? 1 : 0);
            if (vkVer < vkVersions[0].vkVer)
                return $"older than {vkVersions[0].driverVers.First()} ({vulkanVersion})";

            var (version, driverVers) = vkVersions.Last();
            if (vkVer > version)
            {
                if (!autoRefresh)
                    return $"newer than {driverVers.Last()} ({vulkanVersion})";
                    
                await RefreshAsync().ConfigureAwait(false);
                return await GetFromVulkanAsync(vulkanVersion, false).ConfigureAwait(false);
            }
                
            for (var i = 1; i < vkVersions.Count; i++)
            {
                if (vkVer >= vkVersions[i].vkVer)
                    continue;
                    
                var lowerVer = vkVersions[i - 1].vkVer;
                var mapKey = VulkanToDriver.Keys.FirstOrDefault(k => Version.Parse(k) == lowerVer);
                if (mapKey is null)
                    continue;

                if (!VulkanToDriver.TryGetValue(mapKey, out var oldestDriverList))
                    continue;

                return $"unknown version between {oldestDriverList.First()} and {vkVersions[i].driverVers.Last()} ({vulkanVersion})";
            }
        }

        return vulkanVersion;
    }
}