using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using CompatApiClient;

namespace PsnClient
{
    public class CustomTlsCertificatesHandler: HttpClientHandler
    {
        private readonly Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> defaultCertHandler;
        private static readonly X509CertificateCollection CustomCACollecction = new X509Certificate2Collection();
        private static readonly ConcurrentDictionary<string, bool> ValidationCache = new ConcurrentDictionary<string, bool>(1, 5);

        static CustomTlsCertificatesHandler()
        {
            var importedCAs = false;
            try
            {
                var current = Assembly.GetExecutingAssembly();
                var certNames = current.GetManifestResourceNames().Where(cn => cn.ToUpperInvariant().EndsWith(".CER")).ToList();
                if (certNames.Count == 0)
                {
                    ApiConfig.Log.Warn("No embedded Sony root CA certificates were found");
                    return;
                }

                foreach (var resource in certNames)
                {
                    using var stream = current.GetManifestResourceStream(resource);
                    using var memStream = new MemoryStream();
                    stream.CopyTo(memStream);
                    var cert = new X509Certificate2(memStream.ToArray());
                    var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
                    if ((cn?.StartsWith("SCEI DNAS Root") ?? false))
                    {
                        CustomCACollecction.Add(cert);
                        ApiConfig.Log.Debug($"Using Sony root CA with CN '{cn}' for custom certificate validation");
                        importedCAs = true;
                    }
                }
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e, $"Failed to import Sony root CA certificates");
            }
            finally
            {
                if (importedCAs)
                    ApiConfig.Log.Info("Configured custom Sony root CA certificates");
            }
        }

        public CustomTlsCertificatesHandler()
        {
            defaultCertHandler = ServerCertificateCustomValidationCallback;
            ServerCertificateCustomValidationCallback = IgnoreSonyRootCertificates;
        }

        private bool IgnoreSonyRootCertificates(HttpRequestMessage requestMessage, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors policyErrors)
        {
            var issuer = certificate.GetNameInfo(X509NameType.SimpleName, true) ?? "unknown issuer";
            if (issuer.StartsWith("SCEI DNAS Root 0"))
            {
                var thumbprint = certificate.GetCertHashString();
                if (ValidationCache.TryGetValue(thumbprint, out var result))
                    return result;

                result = false;
                try
                {
                    using var customChain = new X509Chain(false);
                    var policy = customChain.ChainPolicy;
                    policy.ExtraStore.AddRange(CustomCACollecction);
                    policy.RevocationMode = X509RevocationMode.NoCheck;
                    if (customChain.Build(certificate) && customChain.ChainStatus.All(s => s.Status == X509ChainStatusFlags.NoError))
                    {
                        ApiConfig.Log.Debug($"Successfully validated certificate {thumbprint} for {requestMessage.RequestUri.Host}");
                        result = true;
                    }
                    if (!result)
                        result = customChain.ChainStatus.All(s => s.Status == X509ChainStatusFlags.UntrustedRoot);
                    if (!result)
                    {
                        ApiConfig.Log.Warn($"Failed to validate certificate {thumbprint} for {requestMessage.RequestUri.Host}");
                        foreach (var s in customChain.ChainStatus)
                            ApiConfig.Log.Debug($"{s.Status}: {s.StatusInformation}");
                    }
                    ValidationCache[thumbprint] = result;
                }
                catch (Exception e)
                {
                    ApiConfig.Log.Error(e, $"Failed to validate certificate {thumbprint} for {requestMessage.RequestUri.Host}");
                }
                return result;
            }
#if DEBUG
            ApiConfig.Log.Debug("Using default certificate validation handler for " + issuer);
#endif
            return defaultCertHandler?.Invoke(requestMessage, certificate, chain, policyErrors) ?? true;
        }
    }
}