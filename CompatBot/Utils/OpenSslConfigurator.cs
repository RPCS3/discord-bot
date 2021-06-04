using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.PlatformAbstractions;

namespace CompatBot.Utils
{
    public static class OpenSslConfigurator
    {
        public static async Task CheckAndFixSystemConfigAsync()
        {
            if (RuntimeEnvironment.OperatingSystemPlatform != Platform.Linux)
                return;

            try
            {
                const string configPath = "/etc/ssl/openssl.cnf";
                Stream stream;
                string content = "";
                if (File.Exists(configPath))
                {
                    stream = File.Open(configPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                    content = await reader.ReadToEndAsync().ConfigureAwait(false);
                    Config.Log.Debug("openssl.cnf content:\n" + content);
                    if (content.Contains("CipherString"))
                    {
                        Config.Log.Debug("No need to configure");
                        return;
                    }
                }
                else
                    stream = File.Open(configPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                await using (stream)
                {
                    await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

                    if (content.Length > 0)
                    {
                        var idx = content.IndexOf("[");
                        if (idx > 0)
                        {
                            await writer.WriteAsync(content[..idx]).ConfigureAwait(false);
                            content = content[idx..];
                        }
                    }
                    
                    await writer.WriteLineAsync("openssl_conf = default_conf").ConfigureAwait(false);
                    await writer.WriteLineAsync("[default_conf]").ConfigureAwait(false);
                    await writer.WriteLineAsync("ssl_conf = ssl_sect").ConfigureAwait(false);
                    await writer.WriteLineAsync("[ssl_sect]").ConfigureAwait(false);
                    await writer.WriteLineAsync("system_default = system_default_sect").ConfigureAwait(false);
                    await writer.WriteLineAsync("[system_default_sect]").ConfigureAwait(false);
                    await writer.WriteLineAsync("CipherString = ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-ECDSA-CHACHA20-POLY1305:ECDHE-RSA-CHACHA20-POLY1305:DHE-RSA-AES128-GCM-SHA256:DHE-RSA-AES256-GCM-SHA384:DHE-RSA-AES128-SHA256:DHE-RSA-AES256-SHA256:AES128-GCM-SHA256:AES256-GCM-SHA384").ConfigureAwait(false);

                    if (content.Length > 0)
                        await writer.WriteAsync(content).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                    Config.Log.Debug("Updated system configuration for OpenSSL");
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Failed to check OpenSSL system configuration");
            }
        }
    }
}