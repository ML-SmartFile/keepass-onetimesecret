using System;
using System.Threading;

namespace OneTimeSecret.Ots
{
    /// <summary>
    /// Wraps a v2 client with a one-shot fallback to v1 per SPEC.md section 8: "404 on a v2
    /// endpoint -> re-run capability detection once, then retry as v1". Only used when the
    /// configured API version is "auto" - a cached "v2" can go stale if a self-hosted
    /// instance is downgraded between the last probe and now.
    ///
    /// Share and Burn both retry once as v1 on a 404, but only Share writes the result to
    /// the cache. A 404 from Burn is at least as likely to mean "this receipt is gone"
    /// (expired, already burned, or from a different host) as "wrong API version" - caching
    /// v1 off that guess would silently downgrade every later Share on this host for up to
    /// 7 days over what might not even be a real version mismatch.
    /// </summary>
    public sealed class AutoFallbackOtsClient : IOtsClient
    {
        private readonly IOtsClient m_primary;
        private readonly Func<IOtsClient> m_fallbackFactory;
        private readonly Action m_onFallback;

        public AutoFallbackOtsClient(IOtsClient primary, Func<IOtsClient> fallbackFactory, Action onFallback)
        {
            if (primary == null) throw new ArgumentNullException("primary");
            if (fallbackFactory == null) throw new ArgumentNullException("fallbackFactory");

            m_primary = primary;
            m_fallbackFactory = fallbackFactory;
            m_onFallback = onFallback;
        }

        public ShareResult Share(ShareRequest request)
        {
            return Share(request, CancellationToken.None);
        }

        public ShareResult Share(ShareRequest request, CancellationToken cancellationToken)
        {
            try
            {
                return m_primary.Share(request, cancellationToken);
            }
            catch (OtsException ex)
            {
                if (ex.StatusCode != 404) throw;

                if (m_onFallback != null) m_onFallback();
                return m_fallbackFactory().Share(request, cancellationToken);
            }
        }

        public BurnResult Burn(string receiptKey, string passphrase, CancellationToken cancellationToken)
        {
            try
            {
                return m_primary.Burn(receiptKey, passphrase, cancellationToken);
            }
            catch (OtsException ex)
            {
                if (ex.StatusCode != 404) throw;

                // Deliberately no m_onFallback() here - see the class remarks above.
                return m_fallbackFactory().Burn(receiptKey, passphrase, cancellationToken);
            }
        }
    }
}
