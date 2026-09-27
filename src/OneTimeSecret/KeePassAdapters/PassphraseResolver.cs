using System;
using KeePassLib;

namespace OneTimeSecret.KeePassAdapters
{
    internal sealed class PassphraseResolution
    {
        public PassphraseResolution(string passphrase, string warning)
        {
            Passphrase = passphrase;
            Warning = warning;
        }

        /// <summary>Null when the stored passphrase could not be read.</summary>
        public string Passphrase { get; private set; }

        /// <summary>Non-null when the user asked for a stored passphrase and did not get one.</summary>
        public string Warning { get; private set; }

        public bool HasPassphrase { get { return !string.IsNullOrEmpty(Passphrase); } }
    }

    /// <summary>
    /// Reads the standing passphrase from the Password field of a chosen entry, at share
    /// time, so the plugin never holds it between shares.
    /// </summary>
    internal static class PassphraseResolver
    {
        /// <summary>
        /// Must be called on the UI thread, before the share dialog opens. A missing or
        /// emptied entry yields a warning rather than an exception - falling back silently
        /// to an unprotected share would be the worst possible failure here.
        /// </summary>
        public static PassphraseResolution Resolve(PluginConfig config, PwDatabase database)
        {
            if (config == null) throw new ArgumentNullException("config");

            PwUuid uuid = config.PassphraseEntryUuid;
            if (uuid == null || uuid.Equals(PwUuid.Zero))
                return new PassphraseResolution(null,
                    "No entry is configured to supply the passphrase. Pick one in the " +
                    "OneTimeSecret options, or choose a different passphrase option.");

            if (database == null || !database.IsOpen)
                return new PassphraseResolution(null,
                    "The database holding the configured passphrase is not open.");

            PwEntry entry = database.RootGroup.FindEntry(uuid, true);
            if (entry == null)
                // Not "no longer exists" - see CredentialResolver's identical fix. The entry
                // may simply live in a different, currently-inactive database.
                return new PassphraseResolution(null,
                    "The entry configured to supply the passphrase was not found in this " +
                    "database. If it lives in a different database, open that one first; " +
                    "otherwise pick a new one in the OneTimeSecret options.");

            string passphrase = EntryFieldReader.ReadResolvedField(entry, database, PwDefs.PasswordField);
            if (string.IsNullOrEmpty(passphrase))
                return new PassphraseResolution(null,
                    "The entry configured to supply the passphrase has an empty password field.");

            return new PassphraseResolution(passphrase, null);
        }

        /// <summary>Display text for the options dialog. Never shows the passphrase itself.</summary>
        public static string Describe(PluginConfig config, PwDatabase database)
        {
            PwUuid uuid = config.PassphraseEntryUuid;
            if (uuid == null || uuid.Equals(PwUuid.Zero)) return "(none selected)";

            if (database == null || !database.IsOpen)
                return "(configured, but the database is not open)";

            PwEntry entry = database.RootGroup.FindEntry(uuid, true);
            if (entry == null) return "(not found in this database - may exist in another)";

            string title = entry.Strings.ReadSafe(PwDefs.TitleField);
            return string.IsNullOrEmpty(title) ? "(untitled entry)" : title;
        }
    }
}
