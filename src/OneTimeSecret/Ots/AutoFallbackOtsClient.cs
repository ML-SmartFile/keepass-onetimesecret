using System;
using System.Threading;

namespace OneTimeSecret.Ots
{
    /// <summary>
    /// Wraps a v2 client with a one-shot fallback to v1 per SPEC.md section 8: "404 on a v2
    /// endpoint -> re-run capability detection once, then retry as v1". Only used when the
    /// configured API version is "auto" - a cached "v2" can go stale if a self-hosted
    /// instance is downgraded between the last probe and now. Applies to both Share and
    /// Burn - v1 has its own (deprecated but present) burn endpoint.
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

        public BurnResult Burn(string receiptKey, CancellationToken cancellationToken)
        {
            try
            {
                return m_primary.Burn(receiptKey, cancellationToken);
            }
            catch (OtsException ex)
            {
                if (ex.StatusCode != 404) throw;

                if (m_onFallback != null) m_onFallback();
                return m_fallbackFactory().Burn(receiptKey, cancellationToken);
            }
        }
    }
}
