using System;
using System.Globalization;
using KeePassLib;
using KeePassLib.Security;

namespace OneTimeSecret.KeePassAdapters
{
    /// <summary>
    /// Writes and reads the receipt of an entry's most recent share, enabling "Burn last
    /// share". Stored as a single protected custom string (SPEC.md section 7) so the
    /// receipt key never appears in KeePass.config.xml or an unprotected export - it is not
    /// the secret itself, but it is enough to burn it, which is sensitive enough to protect.
    /// </summary>
    internal static class ReceiptWriter
    {
        public const string FieldName = "OTS Receipt";
        private const char Separator = '|';

        public static void Write(PwEntry entry, string receiptKey, DateTime utcTimestamp)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            if (string.IsNullOrEmpty(receiptKey)) throw new ArgumentException("receiptKey is required.", "receiptKey");

            string value = receiptKey + Separator + utcTimestamp.ToString("o", CultureInfo.InvariantCulture);
            entry.Strings.Set(FieldName, new ProtectedString(true, value));
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

        public static void Remove(PwEntry entry)
        {
            if (entry == null) return;
            entry.Strings.Remove(FieldName);
            entry.Touch(true);
        }
    }
}
