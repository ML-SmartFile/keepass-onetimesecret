using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace OneTimeSecret.Ots
{
    /// <summary>
    /// HttpWebRequest-based transport. Never touches ServerCertificateValidationCallback -
    /// a TLS failure is surfaced, never bypassed.
    /// </summary>
    public sealed class HttpTransport : IHttpTransport
    {
        private readonly IWebProxy m_proxy;

        /// <summary>Uses whatever proxy the system default supplies.</summary>
        public HttpTransport() : this(null) { }

        /// <summary>
        /// <paramref name="proxy"/> null means "leave the request's default alone". The
        /// caller resolves KeePass's own proxy configuration, since this layer must stay
        /// free of KeePass references.
        /// </summary>
        public HttpTransport(IWebProxy proxy)
        {
            m_proxy = proxy;
        }

        public HttpResult Post(Uri uri, string contentType, byte[] body, string authorizationHeader,
            int timeoutMs, CancellationToken cancellationToken)
        {
            if (uri == null) throw new ArgumentNullException("uri");
            if (body == null) throw new ArgumentNullException("body");

            cancellationToken.ThrowIfCancellationRequested();

            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "POST";
            request.ContentType = contentType;
            request.ContentLength = body.Length;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.UserAgent = "KeePassOneTimeSecret";
            request.Accept = "application/json";
            request.AllowAutoRedirect = false;

            if (m_proxy != null) request.Proxy = m_proxy;

            // Set explicitly rather than via NetworkCredential: pre-emptive Basic avoids the
            // 401 round trip, and a redirect must never carry the token to another host -
            // hence AllowAutoRedirect above stays false.
            if (!string.IsNullOrEmpty(authorizationHeader))
                request.Headers[HttpRequestHeader.Authorization] = authorizationHeader;

            // Abort is the only way to interrupt a blocking HttpWebRequest. It makes the
            // pending call throw, which is translated below.
            using (cancellationToken.Register(delegate { try { request.Abort(); } catch { } }))
            {
                try
                {
                    using (Stream stream = request.GetRequestStream())
                    {
                        stream.Write(body, 0, body.Length);
                    }

                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        return new HttpResult((int)response.StatusCode, ReadBody(response));
                    }
                }
                catch (WebException ex)
                {
                    // Distinguish "we gave up" from "the network failed". The secret may
                    // already exist server-side either way.
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);

                    var errorResponse = ex.Response as HttpWebResponse;
                    if (errorResponse != null)
                    {
                        using (errorResponse)
                        {
                            return new HttpResult((int)errorResponse.StatusCode, ReadBody(errorResponse));
                        }
                    }

                    throw Translate(ex);
                }
            }
        }

        public HttpResult Get(Uri uri, string authorizationHeader, int timeoutMs,
            CancellationToken cancellationToken)
        {
            if (uri == null) throw new ArgumentNullException("uri");

            cancellationToken.ThrowIfCancellationRequested();

            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.UserAgent = "KeePassOneTimeSecret";
            request.Accept = "application/json";
            request.AllowAutoRedirect = false;

            if (m_proxy != null) request.Proxy = m_proxy;

            if (!string.IsNullOrEmpty(authorizationHeader))
                request.Headers[HttpRequestHeader.Authorization] = authorizationHeader;

            using (cancellationToken.Register(delegate { try { request.Abort(); } catch { } }))
            {
                try
                {
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        return new HttpResult((int)response.StatusCode, ReadBody(response));
                    }
                }
                catch (WebException ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);

                    var errorResponse = ex.Response as HttpWebResponse;
                    if (errorResponse != null)
                    {
                        using (errorResponse)
                        {
                            return new HttpResult((int)errorResponse.StatusCode, ReadBody(errorResponse));
                        }
                    }

                    throw Translate(ex);
                }
            }
        }

        private static string ReadBody(HttpWebResponse response)
        {
            using (Stream stream = response.GetResponseStream())
            {
                if (stream == null) return string.Empty;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static HttpTransportException Translate(WebException ex)
        {
            switch (ex.Status)
            {
                case WebExceptionStatus.Timeout:
                    return new HttpTransportException(HttpFailureKind.Timeout,
                        "The request timed out.", ex);

                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                    return new HttpTransportException(HttpFailureKind.NameResolution,
                        "The server name could not be resolved.", ex);

                case WebExceptionStatus.TrustFailure:
                case WebExceptionStatus.SecureChannelFailure:
                    return new HttpTransportException(HttpFailureKind.Tls,
                        "The TLS connection could not be established. The server certificate was not trusted.", ex);

                default:
                    return new HttpTransportException(HttpFailureKind.Network,
                        "Network error: " + ex.Status + ".", ex);
            }
        }

        /// <summary>
        /// Enables TLS 1.2 and, where the platform knows it, TLS 1.3. Called once at plugin
        /// init because older KeePass/.NET combinations still default to SSL3/TLS1.0.
        /// </summary>
        public static void EnableModernTls()
        {
            const SecurityProtocolType tls13 = (SecurityProtocolType)12288;

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | tls13;
            }
            catch (NotSupportedException)
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
        }
    }
}
