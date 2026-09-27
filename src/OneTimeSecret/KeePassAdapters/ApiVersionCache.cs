using System;
using OneTimeSecret.Ots;

namespace OneTimeSecret.KeePassAdapters
{
    /// <summary>
    /// Backs <see cref="IApiVersionCache"/> with CustomConfig, per SPEC.md section 5: cache
    /// the detected API version per base URL, with a timestamp.
    /// </summary>
    internal sealed class ApiVersionCache : IApiVersionCache
    {
        private readonly PluginConfig m_config;

        public ApiVersionCache(PluginConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            m_config = config;
        }

        public string TryGetCachedVersion(string cacheKey, TimeSpan maxAge)
        {
            string version = m_config.DetectedVersion;
            if (string.IsNullOrEmpty(version)) return null;

            if (!string.Equals(m_config.DetectedForUrl, cacheKey, StringComparison.OrdinalIgnoreCase))
                return null;

            DateTime? detectedAt = m_config.DetectedAtUtc;
            if (!detectedAt.HasValue) return null;
            if (DateTime.UtcNow - detectedAt.Value > maxAge) return null;

            return version;
        }

        public void SetCachedVersion(string cacheKey, string version)
        {
            m_config.DetectedForUrl = cacheKey;
            m_config.DetectedVersion = version;
            m_config.DetectedAtUtc = DateTime.UtcNow;
        }
    }
}
