using System;
using System.Globalization;
using KeePass.App.Configuration;
using KeePassLib;
using KeePassLib.Utility;
using OneTimeSecret.Core;

namespace OneTimeSecret.KeePassAdapters
{
    /// <summary>
    /// A pointer to an entry that supplies a credential or passphrase for databases with
    /// none configured of their own - "database" and "entry" are both PwUuids, since a
    /// PwUuid is only unique within the database it came from. Global and local-only: this
    /// is never written into any .kdbx, unlike a database's own reference (see
    /// <see cref="DatabaseScopedConfig"/>) - a database that never has its fallback
    /// database open alongside it must carry no trace of it.
    /// </summary>
    internal sealed class FallbackReference
    {
        public PwUuid DatabaseUuid;
        public PwUuid EntryUuid;

        /// <summary>Cached at set time, so Options can show something meaningful even when
        /// the fallback database is not currently open to read a live name from.</summary>
        public string Display;
    }

    /// <summary>
    /// Typed access to the plugin's settings in KeePass's CustomConfig, per SPEC.md section 6.
    ///
    /// The API token is deliberately absent: only the PwUuid of the entry holding it is
    /// stored, and the credentials are read from that entry at call time. Nothing secret
    /// may ever reach KeePass.config.xml.
    /// </summary>
    internal sealed class PluginConfig
    {
        private const string Prefix = "OneTimeSecret.";

        public const string DefaultBaseUrl = "https://eu.onetimesecret.com";

        public const string ApiVersionAuto = "auto";
        public const string ApiVersionV1 = "v1";
        public const string ApiVersionV2 = "v2";

        private readonly AceCustomConfig m_config;

        public PluginConfig(AceCustomConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            m_config = config;
        }

        public string BaseUrl
        {
            get { return m_config.GetString(Prefix + "BaseUrl", DefaultBaseUrl); }
            set { m_config.SetString(Prefix + "BaseUrl", value); }
        }

        public string ApiVersion
        {
            get { return m_config.GetString(Prefix + "ApiVersion", ApiVersionAuto); }
            set { m_config.SetString(Prefix + "ApiVersion", value); }
        }

        public int DefaultTtlSeconds
        {
            get { return TtlOptions.Sanitize(m_config.GetLong(Prefix + "DefaultTtlSeconds", TtlOptions.DefaultSeconds)); }
            set { m_config.SetLong(Prefix + "DefaultTtlSeconds", value); }
        }

        /// <summary>
        /// Normalized to CRLF on read: the config is XML, and XML parsing collapses CRLF to
        /// a bare LF. A WinForms multi-line TextBox only breaks on CRLF, so without this a
        /// stored multi-line template renders as a single run-on line.
        /// </summary>
        public string Template
        {
            get
            {
                return SecretComposer.NormalizeNewLines(
                    m_config.GetString(Prefix + "Template", SecretComposer.DefaultTemplate));
            }
            set { m_config.SetString(Prefix + "Template", value); }
        }

        public bool IncludeTitle
        {
            get { return m_config.GetBool(Prefix + "IncludeTitle", false); }
            set { m_config.SetBool(Prefix + "IncludeTitle", value); }
        }

        public bool IncludeUserName
        {
            get { return m_config.GetBool(Prefix + "IncludeUserName", true); }
            set { m_config.SetBool(Prefix + "IncludeUserName", value); }
        }

        public bool IncludePassword
        {
            get { return m_config.GetBool(Prefix + "IncludePassword", true); }
            set { m_config.SetBool(Prefix + "IncludePassword", value); }
        }

        public bool IncludeUrl
        {
            get { return m_config.GetBool(Prefix + "IncludeUrl", false); }
            set { m_config.SetBool(Prefix + "IncludeUrl", value); }
        }

        public bool IncludeNotes
        {
            get { return m_config.GetBool(Prefix + "IncludeNotes", false); }
            set { m_config.SetBool(Prefix + "IncludeNotes", value); }
        }

        /// <summary>
        /// The default passphrase mode for new shares. Only the mode is stored - "Enter one
        /// each time" preselects the manual option with an empty box; a stored passphrase
        /// would be a stored secret.
        /// </summary>
        public PassphraseMode PassphraseMode
        {
            get
            {
                return PassphraseModes.Parse(
                    m_config.GetString(Prefix + "PassphraseMode", string.Empty),
                    Core.PassphraseMode.Generate);
            }
            set { m_config.SetString(Prefix + "PassphraseMode", PassphraseModes.ToConfigValue(value)); }
        }

        public bool WriteBackReceipt
        {
            get { return m_config.GetBool(Prefix + "WriteBackReceipt", false); }
            set { m_config.SetBool(Prefix + "WriteBackReceipt", value); }
        }

        public bool AllowInsecureHttp
        {
            get { return m_config.GetBool(Prefix + "AllowInsecureHttp", false); }
            set { m_config.SetBool(Prefix + "AllowInsecureHttp", value); }
        }

        /// <summary>
        /// Pre-M6 global reference, kept for one-time migration into a database's own
        /// CustomData only (see <see cref="KeePassAdapters.DatabaseScopedConfig"/>).
        /// Nothing else reads these after M6 - a PwUuid stored here has no idea which
        /// database it belongs to, which was the whole problem M6 fixed.
        /// </summary>
        public PwUuid LegacyPassphraseEntryUuid
        {
            get { return ReadUuid("PassphraseEntryUuid"); }
        }

        /// <summary>Pre-M6 global reference; see <see cref="LegacyPassphraseEntryUuid"/>.</summary>
        public PwUuid LegacyCredentialEntryUuid
        {
            get { return ReadUuid("CredentialEntryUuid"); }
        }

        /// <summary>
        /// The entry to borrow credentials from when the active database has none of its
        /// own configured. Global and local-only - see <see cref="FallbackReference"/>.
        /// Null means no fallback is configured.
        /// </summary>
        public FallbackReference CredentialFallback
        {
            get { return ReadFallback("CredentialFallback"); }
            set { WriteFallback("CredentialFallback", value); }
        }

        /// <summary>Fallback for the standing-passphrase entry; see <see cref="CredentialFallback"/>.</summary>
        public FallbackReference PassphraseFallback
        {
            get { return ReadFallback("PassphraseFallback"); }
            set { WriteFallback("PassphraseFallback", value); }
        }

        private FallbackReference ReadFallback(string name)
        {
            PwUuid databaseUuid = ReadUuid(name + ".DatabaseUuid");
            if (databaseUuid.Equals(PwUuid.Zero)) return null;

            return new FallbackReference
            {
                DatabaseUuid = databaseUuid,
                EntryUuid = ReadUuid(name + ".EntryUuid"),
                Display = m_config.GetString(Prefix + name + ".Display", string.Empty),
            };
        }

        private void WriteFallback(string name, FallbackReference value)
        {
            if (value == null || value.DatabaseUuid == null || value.DatabaseUuid.Equals(PwUuid.Zero))
            {
                WriteUuid(name + ".DatabaseUuid", PwUuid.Zero);
                WriteUuid(name + ".EntryUuid", PwUuid.Zero);
                m_config.SetString(Prefix + name + ".Display", string.Empty);
                return;
            }

            WriteUuid(name + ".DatabaseUuid", value.DatabaseUuid);
            WriteUuid(name + ".EntryUuid", value.EntryUuid);
            m_config.SetString(Prefix + name + ".Display", value.Display ?? string.Empty);
        }

        private PwUuid ReadUuid(string key)
        {
            string hex = m_config.GetString(Prefix + key, string.Empty);
            if (string.IsNullOrEmpty(hex)) return PwUuid.Zero;

            try
            {
                byte[] bytes = MemUtil.HexStringToByteArray(hex);
                if (bytes == null || bytes.Length != (int)PwUuid.UuidSize) return PwUuid.Zero;
                return new PwUuid(bytes);
            }
            catch (Exception)
            {
                // A hand-edited or truncated value degrades to "not configured" rather than throwing.
                return PwUuid.Zero;
            }
        }

        private void WriteUuid(string key, PwUuid value)
        {
            bool empty = (value == null) || value.Equals(PwUuid.Zero);
            m_config.SetString(Prefix + key, empty ? string.Empty : value.ToHexString());
        }

        public string DetectedVersion
        {
            get { return m_config.GetString(Prefix + "DetectedVersion", string.Empty); }
            set { m_config.SetString(Prefix + "DetectedVersion", value); }
        }

        /// <summary>
        /// The normalized base URL (scheme, host, port and path prefix) <see cref="DetectedVersion"/>
        /// was probed against. Comparing this before trusting the cache is what makes a
        /// stale detection from a previous BaseUrl harmless when it changes, rather than
        /// silently wrong. Keyed on the full URL, not just the host - found by code review:
        /// two path-prefixed instances on the same host (or the same host on two ports, as
        /// in the M5 Docker setup) would otherwise share one cache entry.
        /// </summary>
        public string DetectedForUrl
        {
            get { return m_config.GetString(Prefix + "DetectedForUrl", string.Empty); }
            set { m_config.SetString(Prefix + "DetectedForUrl", value ?? string.Empty); }
        }

        public DateTime? DetectedAtUtc
        {
            get
            {
                string raw = m_config.GetString(Prefix + "DetectedAt", string.Empty);
                if (string.IsNullOrEmpty(raw)) return null;

                DateTime parsed;
                if (!DateTime.TryParseExact(raw, "o", CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out parsed)) return null;

                return parsed.ToUniversalTime();
            }
            set
            {
                m_config.SetString(Prefix + "DetectedAt",
                    value.HasValue ? value.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                                   : string.Empty);
            }
        }
    }
}
