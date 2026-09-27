using System;
using System.Globalization;
using KeePass.App.Configuration;
using KeePassLib;
using KeePassLib.Utility;
using OneTimeSecret.Core;

namespace OneTimeSecret.KeePassAdapters
{
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
        /// Hex PwUuid of the entry whose Password field holds the standing passphrase, used
        /// when PassphraseMode is Entry. Like the credential entry, only the reference is
        /// stored and the value is read at share time.
        /// </summary>
        public PwUuid PassphraseEntryUuid
        {
            get { return ReadUuid("PassphraseEntryUuid"); }
            set { WriteUuid("PassphraseEntryUuid", value); }
        }

        /// <summary>
        /// Hex PwUuid of the entry holding the OTS account e-mail and API token.
        /// Empty means anonymous. Never the token itself.
        /// </summary>
        public PwUuid CredentialEntryUuid
        {
            get { return ReadUuid("CredentialEntryUuid"); }
            set { WriteUuid("CredentialEntryUuid", value); }
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
