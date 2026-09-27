using System;
using System.Net;
using KeePass;
using KeePass.App.Configuration;
using KeePassLib;

namespace OneTimeSecret.KeePassAdapters
{
    /// <summary>
    /// Translates KeePass's proxy configuration (Tools > Options > Advanced) into an
    /// IWebProxy, so shares go through the same proxy as the rest of KeePass.
    /// </summary>
    internal static class ProxyResolver
    {
        /// <summary>
        /// Returns null when KeePass is set to use the system proxy, which is already the
        /// default for a new HttpWebRequest - leaving it alone is the correct behaviour
        /// rather than re-deriving it.
        /// </summary>
        public static IWebProxy Resolve()
        {
            AceIntegration integration = Program.Config.Integration;

            switch (integration.ProxyType)
            {
                case ProxyServerType.None:
                    return new WebProxy();          // empty proxy == direct connection

                case ProxyServerType.Manual:
                    return BuildManualProxy(integration);

                default:
                    return null;                    // System: leave the request default alone
            }
        }

        private static IWebProxy BuildManualProxy(AceIntegration integration)
        {
            string address = integration.ProxyAddress;
            if (string.IsNullOrEmpty(address)) return null;

            string port = integration.ProxyPort;
            string uri = string.IsNullOrEmpty(port) ? address : address + ":" + port;

            // KeePass stores the address without a scheme; WebProxy needs one to parse.
            if (uri.IndexOf("://", StringComparison.Ordinal) < 0) uri = "http://" + uri;

            WebProxy proxy;
            try
            {
                proxy = new WebProxy(uri);
            }
            catch (UriFormatException)
            {
                // A malformed proxy setting must not stop a share; fall back to the default.
                return null;
            }

            switch (integration.ProxyAuthType)
            {
                case ProxyAuthType.None:
                    proxy.UseDefaultCredentials = false;
                    proxy.Credentials = null;
                    break;

                case ProxyAuthType.Manual:
                    proxy.UseDefaultCredentials = false;
                    proxy.Credentials = new NetworkCredential(
                        integration.ProxyUserName ?? string.Empty,
                        integration.ProxyPassword ?? string.Empty);
                    break;

                default:
                    proxy.UseDefaultCredentials = true;
                    break;
            }

            return proxy;
        }
    }
}
