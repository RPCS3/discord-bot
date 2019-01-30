using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace PsnClient
{
    public class CustomTlsCertificatesHandler: HttpClientHandler
    {
        private readonly Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> defaultCertHandler;

        public CustomTlsCertificatesHandler()
        {
            defaultCertHandler = ServerCertificateCustomValidationCallback;
            ServerCertificateCustomValidationCallback = IgnoreSonyRootCertificates;
        }

        private bool IgnoreSonyRootCertificates(HttpRequestMessage requestMessage, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors policyErrors)
        {
            //todo: do proper checks with root certs from ps3 fw
            if (certificate.IssuerName.Name?.StartsWith("SCEI DNAS Root 0") ?? false)
                return true;

            return defaultCertHandler?.Invoke(requestMessage, certificate, chain, policyErrors) ?? true;
        }
    }
}