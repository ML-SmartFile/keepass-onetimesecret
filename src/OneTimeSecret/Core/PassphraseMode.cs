using System;

namespace OneTimeSecret.Core
{
    /// <summary>How a share is protected. Only the mode is ever persisted - a stored
    /// passphrase would be a stored secret, which this plugin does not do.</summary>
    public enum PassphraseMode
    {
        None,
        Generate,
        Manual,

        /// <summary>
        /// Read from the Password field of a chosen database entry, at share time.
        /// Weaker than Generate by design: one passphrase reused across every share means
        /// anyone who received one share can open all later ones, and it never rotates.
        /// Only sensible with a single counterparty you agreed it with out of band.
        /// </summary>
        Entry,
    }

    public static class PassphraseModes
    {
        public const string NoneValue = "none";
        public const string GenerateValue = "generate";
        public const string ManualValue = "manual";
        public const string EntryValue = "entry";

        public static string ToConfigValue(PassphraseMode mode)
        {
            switch (mode)
            {
                case PassphraseMode.None: return NoneValue;
                case PassphraseMode.Manual: return ManualValue;
                case PassphraseMode.Entry: return EntryValue;
                default: return GenerateValue;
            }
        }

        public static PassphraseMode Parse(string value, PassphraseMode fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;

            if (string.Equals(value, NoneValue, StringComparison.OrdinalIgnoreCase)) return PassphraseMode.None;
            if (string.Equals(value, ManualValue, StringComparison.OrdinalIgnoreCase)) return PassphraseMode.Manual;
            if (string.Equals(value, EntryValue, StringComparison.OrdinalIgnoreCase)) return PassphraseMode.Entry;
            if (string.Equals(value, GenerateValue, StringComparison.OrdinalIgnoreCase)) return PassphraseMode.Generate;

            return fallback;
        }

        /// <summary>Labels for the dropdowns, in mode order.</summary>
        public static string Describe(PassphraseMode mode)
        {
            switch (mode)
            {
                case PassphraseMode.None: return "None";
                case PassphraseMode.Manual: return "Enter one each time";
                case PassphraseMode.Entry: return "Passphrase from database";
                default: return "Generate one";
            }
        }
    }
}
