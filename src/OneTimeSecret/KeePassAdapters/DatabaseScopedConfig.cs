using System;
using KeePassLib;
using KeePassLib.Utility;

namespace OneTimeSecret.KeePassAdapters
{
    /// <summary>
    /// A database's own credential/passphrase entry reference, stored in that database's
    /// own <see cref="PwDatabase.CustomData"/> rather than in KeePass's global CustomConfig.
    /// This is deliberate: the value travels inside the .kdbx itself (works automatically
    /// on another PC with the plugin installed, and survives a rename/move of the file),
    /// and - unlike the fallback pointer in <see cref="PluginConfig"/> - it is fine for
    /// this to be visible to anyone who receives a copy of the database, since it names no
    /// other database and carries no secret, only a PwUuid meaningful within this file.
    ///
    /// Persisting a change here requires saving the database (Ctrl+S), same as any other
    /// KeePass edit - there is no separate "config save."
    /// </summary>
    internal static class DatabaseScopedConfig
    {
        private const string CredentialKey = "OneTimeSecret.CredentialEntryUuid";
        private const string PassphraseKey = "OneTimeSecret.PassphraseEntryUuid";
        private const string MigratedKey = "OneTimeSecret.Migrated";

        public static PwUuid GetCredentialEntryUuid(PwDatabase database)
        {
            return ReadUuid(database, CredentialKey);
        }

        public static void SetCredentialEntryUuid(PwDatabase database, PwUuid value)
        {
            WriteUuid(database, CredentialKey, value);
        }

        public static PwUuid GetPassphraseEntryUuid(PwDatabase database)
        {
            return ReadUuid(database, PassphraseKey);
        }

        public static void SetPassphraseEntryUuid(PwDatabase database, PwUuid value)
        {
            WriteUuid(database, PassphraseKey, value);
        }

        /// <summary>
        /// True until <see cref="MarkMigrated"/> has been called once for this database.
        /// Guards the one-time copy of the pre-M6 global reference into this database's own
        /// CustomData, so an explicit later "Clear" (which removes the key, making it look
        /// identical to "never configured") is never mistaken for "not yet migrated" and
        /// silently repopulated from the stale legacy value.
        /// </summary>
        public static bool NeedsMigration(PwDatabase database)
        {
            if (database == null) return false;
            return database.CustomData.Get(MigratedKey) == null;
        }

        public static void MarkMigrated(PwDatabase database)
        {
            if (database == null) return;
            database.CustomData.Set(MigratedKey, "true");
        }

        private static PwUuid ReadUuid(PwDatabase database, string key)
        {
            if (database == null) return PwUuid.Zero;

            string hex = database.CustomData.Get(key);
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

        private static void WriteUuid(PwDatabase database, string key, PwUuid value)
        {
            if (database == null) return;

            bool empty = (value == null) || value.Equals(PwUuid.Zero);
            if (empty) database.CustomData.Remove(key);
            else database.CustomData.Set(key, value.ToHexString());
        }
    }
}
