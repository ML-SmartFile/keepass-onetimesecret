using System;
using System.Threading;

namespace OneTimeSecret.Ots
{
    /// <summary>
    /// The seam that keeps the clients testable without network access.
    /// HTTP error statuses come back as a result so the caller can read the API's error
    /// body; only transport-level failures throw.
    /// </summary>
    public interface IHttpTransport
    {
        /// <summary>
        /// Posts a body. <paramref name="authorizationHeader"/> is null for anonymous
        /// requests; when set it is sent verbatim as the Authorization header.
        ///
        /// Throws <see cref="OperationCanceledException"/> if the token is cancelled. Note
        /// that cancelling only stops us waiting - the server may already have created the
        /// secret, and callers must not imply otherwise.
        /// </summary>
        HttpResult Post(Uri uri, string contentType, byte[] body, string authorizationHeader,
            int timeoutMs, CancellationToken cancellationToken);
    }

    public sealed class HttpResult
    {
        public HttpResult(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body ?? string.Empty;
        }

        public int StatusCode { get; private set; }
        public string Body { get; private set; }

        public bool IsSuccess { get { return StatusCode >= 200 && StatusCode < 300; } }
    }

    public enum HttpFailureKind
    {
        Network,
        Timeout,
        NameResolution,
        Tls
    }

    public sealed class HttpTransportException : Exception
    {
        public HttpTransportException(HttpFailureKind kind, string message, Exception inner)
            : base(message, inner)
        {
            Kind = kind;
        }

        public HttpFailureKind Kind { get; private set; }
    }
}
