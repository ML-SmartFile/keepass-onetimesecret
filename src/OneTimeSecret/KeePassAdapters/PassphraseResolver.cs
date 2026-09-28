using System;
using System.Collections.Generic;
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
    /// time, so the plugin never holds it between shares. Resolution order mirrors
    /// <see cref="CredentialResolver"/> exactly: this database's own entry first, then the
    /// global fallback entry if this database has none configured and the fallback
    /// database is currently open. See CredentialResolver's summary for why a broken own
    /// reference does not fall through to the fallback.
    /// </summary>
    internal static class PassphraseResolver
    {
        /// <summary>
        /// Must be called on the UI thread, before the share dialog opens. A missing or
        /// emptied entry yields a warning rather than an exception - falling back silently
        /// to an unprotected share would be the worst possible failure here.
        /// </summary>
        public static PassphraseResolution Resolve(
            PluginConfig config, PwDatabase database, IEnumerable<PwDatabase> openDatabases)
        {
            if (config == null) throw new ArgumentNullException("config");

            if (database == null || !database.IsOpen)
                return new PassphraseResolution(null,
                    "The database holding the configured passphrase is not open.");

            PwUuid uuid = DatabaseScopedConfig.GetPassphraseEntryUuid(database);
            if (!uuid.Equals(PwUuid.Zero))
            {
                PwEntry entry = database.RootGroup.FindEntry(uuid, true);
                if (entry == null)
                    return new PassphraseResolution(null,
                        "The entry configured to supply the passphrase no longer exists in " +
                        "this database. Pick a new one in the OneTimeSecret options.");

                return FromEntry(entry, database);
            }

            return ResolveFallback(config.PassphraseFallback, openDatabases);
        }

        private static PassphraseResolution ResolveFallback(
            FallbackReference fallback, IEnumerable<PwDatabase> openDatabases)
        {
            if (fallback == null)
                return new PassphraseResolution(null,
                    "No entry is configured to supply the passphrase. Pick one in the " +
                    "OneTimeSecret options, or choose a different passphrase option.");

            PwDatabase fallbackDatabase = CredentialResolver.FindOpenDatabase(fallback.DatabaseUuid, openDatabases);
            PwEntry entry = (fallbackDatabase != null)
                ? fallbackDatabase.RootGroup.FindEntry(fallback.EntryUuid, true)
                : null;

            if (entry == null)
                return new PassphraseResolution(null,
                    "Your fallback passphrase database" +
                    (string.IsNullOrEmpty(fallback.Display) ? "" : " (\"" + fallback.Display + "\")") +
                    " is not currently open.");

            return FromEntry(entry, fallbackDatabase);
        }

        private static PassphraseResolution FromEntry(PwEntry entry, PwDatabase database)
        {
            string passphrase = EntryFieldReader.ReadResolvedField(entry, database, PwDefs.PasswordField);
            if (string.IsNullOrEmpty(passphrase))
                return new PassphraseResolution(null,
                    "The entry configured to supply the passphrase has an empty password field.");

            return new PassphraseResolution(passphrase, null);
        }

        /// <summary>Display text for the options dialog. Never shows the passphrase itself.</summary>
        public static string Describe(PluginConfig config, PwDatabase database)
        {
            if (database == null || !database.IsOpen)
                return "(open a database to configure)";

            PwUuid uuid = DatabaseScopedConfig.GetPassphraseEntryUuid(database);
            if (uuid.Equals(PwUuid.Zero)) return "(none - using the fallback below, if any)";

            PwEntry entry = database.RootGroup.FindEntry(uuid, true);
            if (entry == null) return "(no longer exists in this database)";

            string title = entry.Strings.ReadSafe(PwDefs.TitleField);
            return string.IsNullOrEmpty(title) ? "(untitled entry)" : title;
        }

        /// <summary>Display text for the fallback status line. Never shows the passphrase.</summary>
        public static string DescribeFallback(PluginConfig config, IEnumerable<PwDatabase> openDatabases)
        {
            FallbackReference fallback = config.PassphraseFallback;
            if (fallback == null) return "(none)";

            bool open = CredentialResolver.FindOpenDatabase(fallback.DatabaseUuid, openDatabases) != null;
            string display = string.IsNullOrEmpty(fallback.Display) ? "(unnamed database)" : fallback.Display;
            return open ? display : display + "  -  not currently open";
        }
    }
}
