using System;
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
    /// </summary>
    internal static class CredentialResolver
    {
        /// <summary>
        /// Must be called on the UI thread, before dispatching a request. A configured entry
        /// that has since been deleted, emptied, or locked away degrades to anonymous with a
        /// warning - never an exception.
        /// </summary>
        public static CredentialResolution Resolve(PluginConfig config, PwDatabase database)
        {
            if (config == null) throw new ArgumentNullException("config");

            PwUuid uuid = config.CredentialEntryUuid;
            if (uuid == null || uuid.Equals(PwUuid.Zero))
                return new CredentialResolution(OtsCredentials.Anonymous, null);

            if (database == null || !database.IsOpen)
                return new CredentialResolution(OtsCredentials.Anonymous,
                    "The database holding your OneTimeSecret credentials is not open. " +
                    "Sharing anonymously instead.");

            PwEntry entry = database.RootGroup.FindEntry(uuid, true);
            if (entry == null)
                // Not "no longer exists": the entry is only missing from the currently open
                // database. A user working with more than one .kdbx would otherwise see this
                // as a false "deleted" warning for an entry that is still sitting in a
                // different, perfectly intact database. Found by code review.
                return new CredentialResolution(OtsCredentials.Anonymous,
                    "The entry configured for your OneTimeSecret credentials was not found in " +
                    "this database. If it lives in a different database, open that one first; " +
                    "otherwise pick a new entry in the OneTimeSecret options. " +
                    "Sharing anonymously instead.");

            string user = EntryFieldReader.ReadResolvedField(entry, database, PwDefs.UserNameField);
            string token = EntryFieldReader.ReadResolvedField(entry, database, PwDefs.PasswordField);

            var credentials = new OtsCredentials(user, token);
            if (credentials.IsAnonymous)
                return new CredentialResolution(OtsCredentials.Anonymous,
                    "The entry configured for your OneTimeSecret credentials has no user name or " +
                    "password. Sharing anonymously instead.");

            return new CredentialResolution(credentials, null);
        }

        /// <summary>Display text for the options dialog. Never shows the token.</summary>
        public static string Describe(PluginConfig config, PwDatabase database)
        {
            PwUuid uuid = config.CredentialEntryUuid;
            if (uuid == null || uuid.Equals(PwUuid.Zero))
                return "(none - sharing anonymously)";

            if (database == null || !database.IsOpen)
                return "(configured, but the database is not open)";

            PwEntry entry = database.RootGroup.FindEntry(uuid, true);
            if (entry == null) return "(not found in this database - may exist in another)";

            string title = entry.Strings.ReadSafe(PwDefs.TitleField);
            string user = entry.Strings.ReadSafe(PwDefs.UserNameField);

            if (string.IsNullOrEmpty(title)) title = "(untitled entry)";
            return string.IsNullOrEmpty(user) ? title : title + "  -  " + user;
        }
    }
}
