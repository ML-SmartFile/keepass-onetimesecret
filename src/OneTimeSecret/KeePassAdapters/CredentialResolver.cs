using System;
using System.Collections.Generic;
using KeePassLib;
using OneTimeSecret.Ots;

namespace OneTimeSecret.KeePassAdapters
{
    internal sealed class CredentialResolution
    {
        public CredentialResolution(OtsCredentials credentials, string warning)
        {
            Credentials = credentials;
            Warning = warning;
        }

        public OtsCredentials Credentials { get; private set; }

        /// <summary>Non-null when the share proceeds anonymously against the user's intent.</summary>
        public string Warning { get; private set; }
    }

    /// <summary>
    /// Turns the configured PwUuid into live credentials, reading them from the entry at
    /// call time so the token is never held by this plugin between shares.
    ///
    /// Resolution order: (1) this database's own entry, stored in its own CustomData -
    /// see <see cref="DatabaseScopedConfig"/>; (2) if this database has none configured,
    /// the global fallback entry, but only when the database holding it is currently open
    /// in another tab. A database's own reference that points at a *deleted* entry fails
    /// loudly rather than silently falling through to the fallback - the user configured
    /// this database explicitly and should hear that it broke, not get a different
    /// credential they never asked this database to use.
    /// </summary>
    internal static class CredentialResolver
    {
        /// <summary>
        /// Must be called on the UI thread, before dispatching a request. A configured entry
        /// that has since been deleted, emptied, or locked away degrades to anonymous with a
        /// warning - never an exception.
        /// </summary>
        public static CredentialResolution Resolve(
            PluginConfig config, PwDatabase database, IEnumerable<PwDatabase> openDatabases)
        {
            if (config == null) throw new ArgumentNullException("config");

            if (database == null || !database.IsOpen)
                return new CredentialResolution(OtsCredentials.Anonymous, null);

            PwUuid uuid = DatabaseScopedConfig.GetCredentialEntryUuid(database);
            if (!uuid.Equals(PwUuid.Zero))
            {
                PwEntry entry = database.RootGroup.FindEntry(uuid, true);
                if (entry == null)
                    return new CredentialResolution(OtsCredentials.Anonymous,
                        "The entry configured for your OneTimeSecret credentials no longer " +
                        "exists in this database. Pick a new entry in the OneTimeSecret " +
                        "options. Sharing anonymously instead.");

                return FromEntry(entry, database);
            }

            return ResolveFallback(config.CredentialFallback, openDatabases);
        }

        private static CredentialResolution ResolveFallback(
            FallbackReference fallback, IEnumerable<PwDatabase> openDatabases)
        {
            if (fallback == null)
                return new CredentialResolution(OtsCredentials.Anonymous, null);

            PwDatabase fallbackDatabase = FindOpenDatabase(fallback.DatabaseUuid, openDatabases);
            PwEntry entry = (fallbackDatabase != null)
                ? fallbackDatabase.RootGroup.FindEntry(fallback.EntryUuid, true)
                : null;

            if (entry == null)
                return new CredentialResolution(OtsCredentials.Anonymous,
                    "Your fallback OneTimeSecret credentials database" +
                    (string.IsNullOrEmpty(fallback.Display) ? "" : " (\"" + fallback.Display + "\")") +
                    " is not currently open. Sharing anonymously instead.");

            return FromEntry(entry, fallbackDatabase);
        }

        private static CredentialResolution FromEntry(PwEntry entry, PwDatabase database)
        {
            string user = EntryFieldReader.ReadResolvedField(entry, database, PwDefs.UserNameField);
            string token = EntryFieldReader.ReadResolvedField(entry, database, PwDefs.PasswordField);

            var credentials = new OtsCredentials(user, token);
            if (credentials.IsAnonymous)
                return new CredentialResolution(OtsCredentials.Anonymous,
                    "The entry configured for your OneTimeSecret credentials has no user name or " +
                    "password. Sharing anonymously instead.");

            return new CredentialResolution(credentials, null);
        }

        internal static PwDatabase FindOpenDatabase(PwUuid databaseUuid, IEnumerable<PwDatabase> openDatabases)
        {
            if (databaseUuid == null || databaseUuid.Equals(PwUuid.Zero) || openDatabases == null) return null;

            foreach (PwDatabase candidate in openDatabases)
            {
                if (candidate != null && candidate.IsOpen && candidate.RootGroup != null &&
                    candidate.RootGroup.Uuid.Equals(databaseUuid))
                    return candidate;
            }
            return null;
        }

        /// <summary>Display text for the options dialog. Never shows the token.</summary>
        public static string Describe(PluginConfig config, PwDatabase database)
        {
            if (database == null || !database.IsOpen)
                return "(open a database to configure)";

            PwUuid uuid = DatabaseScopedConfig.GetCredentialEntryUuid(database);
            if (uuid.Equals(PwUuid.Zero)) return "(none - using the fallback below, if any)";

            PwEntry entry = database.RootGroup.FindEntry(uuid, true);
            if (entry == null) return "(no longer exists in this database)";

            string title = entry.Strings.ReadSafe(PwDefs.TitleField);
            string user = entry.Strings.ReadSafe(PwDefs.UserNameField);

            if (string.IsNullOrEmpty(title)) title = "(untitled entry)";
            return string.IsNullOrEmpty(user) ? title : title + "  -  " + user;
        }

        /// <summary>Display text for the fallback status line. Never shows the token.</summary>
        public static string DescribeFallback(PluginConfig config, IEnumerable<PwDatabase> openDatabases)
        {
            FallbackReference fallback = config.CredentialFallback;
            if (fallback == null) return "(none)";

            bool open = FindOpenDatabase(fallback.DatabaseUuid, openDatabases) != null;
            string display = string.IsNullOrEmpty(fallback.Display) ? "(unnamed database)" : fallback.Display;
            return open ? display : display + "  -  not currently open";
        }
    }
}
