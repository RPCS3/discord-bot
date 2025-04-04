﻿using System.IO;
using System.Runtime.InteropServices;

namespace CompatBot.Utils;

public static class OpenSslConfigurator
{
    public static async Task CheckAndFixSystemConfigAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
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
#if DEBUG
                Config.Log.Debug("openssl.cnf content:\n" + content);
#endif                    
                if (content.Contains("CipherString") && content.Contains("\nopenssl_conf"))
                {
                    Config.Log.Debug("No need to configure");
                    return;
                }
                stream.Seek(0, SeekOrigin.Begin);
            }
            else
                stream = File.Open(configPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using (stream)
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

                if (content.Length > 0)
                {
                    if (!content.Contains("\nopenssl_conf"))
                    {
                        var cutStart = content.IndexOf("openssl_conf");
                        if (cutStart > 0)
                        {
                            var cutEnd = content.IndexOf("CipherString", cutStart);
                            cutEnd = content.IndexOf('\n', cutEnd) + 1;
                            content = content[..cutStart] + content[cutEnd..];
                        }
                    }
                        
                    var idx = content.IndexOf("\n[");
                    if (idx > 0)
                    {
                        idx++;
                        await writer.WriteAsync(content[..idx]).ConfigureAwait(false);
                        content = content[idx..];
                    }
                }
                    
                await writer.WriteLineAsync("""
                    openssl_conf = default_conf

                    [default_conf]
                    ssl_conf = ssl_sect

                    [ssl_sect]
                    system_default = system_default_sect

                    [system_default_sect]
                    CipherString = ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-ECDSA-CHACHA20-POLY1305:ECDHE-RSA-CHACHA20-POLY1305:DHE-RSA-AES128-GCM-SHA256:DHE-RSA-AES256-GCM-SHA384:DHE-RSA-AES128-SHA256:DHE-RSA-AES256-SHA256:AES128-GCM-SHA256:AES256-GCM-SHA384
                    """).ConfigureAwait(false);
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