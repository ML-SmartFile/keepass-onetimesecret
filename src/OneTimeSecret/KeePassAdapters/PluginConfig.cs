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

        public string Template
        {
            get { return m_config.GetString(Prefix + "Template", SecretComposer.DefaultTemplate); }
            set { m_config.SetString(Prefix + "Template", value); }
        }

        public string CustomMessage
        {
            get { return m_config.GetString(Prefix + "CustomMessage", string.Empty); }
            set { m_config.SetString(Prefix + "CustomMessage", value); }
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

        public bool GeneratePassphrase
        {
            get { return m_config.GetBool(Prefix + "GeneratePassphrase", true); }
            set { m_config.SetBool(Prefix + "GeneratePassphrase", value); }
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
        /// Hex PwUuid of the entry holding the OTS account e-mail and API token.
        /// Empty means anonymous. Never the token itself.
        /// </summary>
        public PwUuid CredentialEntryUuid
        {
            get
            {
                string hex = m_config.GetString(Prefix + "CredentialEntryUuid", string.Empty);
                if (string.IsNullOrEmpty(hex)) return PwUuid.Zero;

                try
                {
                    byte[] bytes = MemUtil.HexStringToByteArray(hex);
                    if (bytes == null || bytes.Length != (int)PwUuid.UuidSize) return PwUuid.Zero;
                    return new PwUuid(bytes);
                }
                catch (Exception)
                {
                    // A hand-edited or truncated value degrades to anonymous rather than throwing.
                    return PwUuid.Zero;
                }
            }
            set
            {
                bool empty = (value == null) || value.Equals(PwUuid.Zero);
                m_config.SetString(Prefix + "CredentialEntryUuid", empty ? string.Empty : value.ToHexString());
            }
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
