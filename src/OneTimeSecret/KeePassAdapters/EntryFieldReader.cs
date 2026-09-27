using System;
using System.Collections.Generic;
using KeePass.Util.Spr;
using KeePassLib;
using OneTimeSecret.Core;

namespace OneTimeSecret.KeePassAdapters
{
    /// <summary>
    /// Reads entry fields and resolves KeePass placeholders. This is the only place
    /// SprEngine is touched, which keeps Core/ and Ots/ free of KeePass references.
    /// </summary>
    internal static class EntryFieldReader
    {
        /// <summary>
        /// Must be called on the UI thread, before any request is dispatched - the database
        /// can lock mid-flight, so every value the request needs is materialized up front.
        /// </summary>
        public static Dictionary<string, string> ReadResolvedFields(PwEntry entry, PwDatabase database)
        {
            if (entry == null) throw new ArgumentNullException("entry");

            // NonActive is All minus Active: it still dereferences {REF:...} and entry strings,
            // but excludes {CMD:} execution, the character-picker prompt, and the state-changing
            // NewPassword / HmacOtp placeholders. Composing a secret must not run anything,
            // prompt anyone, or burn an OTP counter.
            var ctx = new SprContext(entry, database, SprCompileFlags.NonActive);

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            fields[SecretComposer.FieldTitle] = Resolve(entry, PwDefs.TitleField, ctx);
            fields[SecretComposer.FieldUserName] = Resolve(entry, PwDefs.UserNameField, ctx);
            fields[SecretComposer.FieldPassword] = Resolve(entry, PwDefs.PasswordField, ctx);
            fields[SecretComposer.FieldUrl] = Resolve(entry, PwDefs.UrlField, ctx);
            fields[SecretComposer.FieldNotes] = Resolve(entry, PwDefs.NotesField, ctx);
            return fields;
        }

        private static string Resolve(PwEntry entry, string fieldName, SprContext ctx)
        {
            string raw = entry.Strings.ReadSafe(fieldName);
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            return SprEngine.Compile(raw, ctx) ?? string.Empty;
        }
    }
}
