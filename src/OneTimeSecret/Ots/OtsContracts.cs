using System;
using System.Threading;

namespace OneTimeSecret.Ots
{
    public sealed class ShareRequest
    {
        public ShareRequest(string secret, int ttlSeconds)
            : this(secret, ttlSeconds, null, null) { }

        public ShareRequest(string secret, int ttlSeconds, string passphrase, string recipient)
        {
            if (secret == null) throw new ArgumentNullException("secret");
            Secret = secret;
            TtlSeconds = ttlSeconds;
            Passphrase = passphrase;
            Recipient = recipient;
        }

        public string Secret { get; private set; }
        public int TtlSeconds { get; private set; }

        /// <summary>
        /// Optional. When set, the recipient must enter it to reveal the secret. It is not
        /// part of the share link and must be delivered over a different channel.
        /// </summary>
        public string Passphrase { get; private set; }

        /// <summary>Optional e-mail address; OneTimeSecret delivers the link itself.</summary>
        public string Recipient { get; private set; }

        public bool HasPassphrase { get { return !string.IsNullOrEmpty(Passphrase); } }
    }

    public sealed class ShareResult
    {
        public ShareResult(string secretKey, string metadataKey, string shareUrl, string receiptUrl,
            int requestedTtlSeconds, int effectiveTtlSeconds)
        {
            SecretKey = secretKey;
            MetadataKey = metadataKey;
            ShareUrl = shareUrl;
            ReceiptUrl = receiptUrl;
            RequestedTtlSeconds = requestedTtlSeconds;
            EffectiveTtlSeconds = effectiveTtlSeconds;
        }

        public string SecretKey { get; private set; }
        public string MetadataKey { get; private set; }
        public string ShareUrl { get; private set; }
        public string ReceiptUrl { get; private set; }

        public int RequestedTtlSeconds { get; private set; }

        /// <summary>
        /// The lifetime the server actually applied, or 0 when it did not say. The hosted
        /// service silently clamps anonymous shares to 7 days instead of rejecting a longer
        /// request, so this is the only honest source for what the user really got.
        /// </summary>
        public int EffectiveTtlSeconds { get; private set; }

        public bool TtlWasShortened
        {
            get
            {
                return EffectiveTtlSeconds > 0
                    && RequestedTtlSeconds > 0
                    && EffectiveTtlSeconds < RequestedTtlSeconds;
            }
        }
    }

    /// <summary>
    /// Outcome of a burn attempt. <see cref="Burned"/> false is a normal, non-exceptional
    /// result the API itself reports (v2: HTTP 200 with "success":false; v1: HTTP 200 with
    /// a "state" that isn't "burned") rather than an HTTP error. <see cref="Detail"/>
    /// explains why when that is known.
    /// </summary>
    public sealed class BurnResult
    {
        public BurnResult(bool burned, string detail)
        {
            Burned = burned;
            Detail = detail;
        }

        public bool Burned { get; private set; }
        public string Detail { get; private set; }
    }

    public interface IOtsClient
    {
        ShareResult Share(ShareRequest request);

        /// <summary>
        /// Cancellation stops us waiting for the response. It cannot un-create a secret the
        /// server may already have stored, and callers must not imply that it did.
        /// </summary>
        ShareResult Share(ShareRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Burns a previously-shared secret by its receipt key. Both v1 and v2 support this
        /// (v1's is deprecated but present) - see SPEC.md section 5.
        ///
        /// <paramref name="passphrase"/> may be null/empty: the burn endpoint's own
        /// description text says it requires the passphrase when one was set, so the caller
        /// supplies the exact one that share used (stored alongside the receipt itself, in
        /// the KeePassAdapters layer, and cleared together with it).
        /// </summary>
        BurnResult Burn(string receiptKey, string passphrase, CancellationToken cancellationToken);
    }

    /// <summary>
    /// A failure with a message fit to show the user. Never carries the shared secret:
    /// messages are built from status codes and the API's own error text only.
    /// </summary>
    public sealed class OtsException : Exception
    {
        public OtsException(string message) : this(message, 0, null, null) { }

        public OtsException(string message, int statusCode, string requestId, Exception inner)
            : base(message, inner)
        {
            StatusCode = statusCode;
            RequestId = requestId;
        }

        /// <summary>HTTP status, or 0 when the failure happened below the HTTP layer.</summary>
        public int StatusCode { get; private set; }

        /// <summary>Server-supplied correlation id, when the API returned one.</summary>
        public string RequestId { get; private set; }
    }
}
