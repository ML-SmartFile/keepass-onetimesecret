using System;
using System.Globalization;
using KeePassLib;
using KeePassLib.Security;

namespace OneTimeSecret.KeePassAdapters
{
    /// <summary>
    /// Writes and reads the receipt of an entry's most recent share, enabling "Burn last
    /// share". Stored as protected custom strings (SPEC.md section 7) so neither field ever
    /// appears in KeePass.config.xml or an unprotected export.
    ///
    /// The passphrase field exists because burning a passphrase-protected secret requires
    /// the exact passphrase that share used (SPEC.md section 5) - confirmed by testing that
    /// this holds for every passphrase mode, not just a standing entry's, which is the only
    /// one re-derivable from current config. Storing it here, in the same protected field
    /// mechanism already used for the receipt itself, keeps it inside the user's own
    /// encrypted database rather than inventing a new persistence path - not the
    /// KeePass.config.xml problem non-negotiable #5 rules out, since it never leaves the
    /// entry. Only ever the passphrase this specific share used; cleared together with the
    /// receipt so nothing outlives the thing it was for.
    /// </summary>
    internal static class ReceiptWriter
    {
        public const string FieldName = "OTS Receipt";
        public const string PassphraseFieldName = "OTS Passphrase";
        private const char Separator = '|';

        public static void Write(PwEntry entry, string receiptKey, DateTime utcTimestamp, string passphrase)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            if (string.IsNullOrEmpty(receiptKey)) throw new ArgumentException("receiptKey is required.", "receiptKey");

            string value = receiptKey + Separator + utcTimestamp.ToString("o", CultureInfo.InvariantCulture);
            entry.Strings.Set(FieldName, new ProtectedString(true, value));

            // Cleared, not left stale, when this share has none - an old passphrase from a
            // previous share on a reused entry must not be offered for a burn it does not
            // belong to.
            if (!string.IsNullOrEmpty(passphrase))
                entry.Strings.Set(PassphraseFieldName, new ProtectedString(true, passphrase));
            else
                entry.Strings.Remove(PassphraseFieldName);

            entry.Touch(true);
        }

        public static bool HasReceipt(PwEntry entry)
        {
            return entry != null && entry.Strings.Exists(FieldName);
        }

        /// <summary>Returns null if the field is absent or empty.</summary>
        public static string TryReadReceiptKey(PwEntry entry)
        {
            if (entry == null) return null;

            ProtectedString ps = entry.Strings.Get(FieldName);
            if (ps == null) return null;

            string raw = ps.ReadString();
            if (string.IsNullOrEmpty(raw)) return null;

            int sep = raw.IndexOf(Separator);
            return sep > 0 ? raw.Substring(0, sep) : raw;
        }

        /// <summary>Returns null if this share had no passphrase, or predates this field.</summary>
        public static string TryReadPassphrase(PwEntry entry)
        {
            if (entry == null) return null;

            ProtectedString ps = entry.Strings.Get(PassphraseFieldName);
            if (ps == null) return null;

            string raw = ps.ReadString();
            return string.IsNullOrEmpty(raw) ? null : raw;
        }

        public static void Remove(PwEntry entry)
        {
            if (entry == null) return;
            entry.Strings.Remove(FieldName);
            entry.Strings.Remove(PassphraseFieldName);
            entry.Touch(true);
        }
    }
}
