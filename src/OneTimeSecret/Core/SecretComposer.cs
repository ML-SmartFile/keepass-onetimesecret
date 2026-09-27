using System;
using System.Collections.Generic;
using System.Text;

namespace OneTimeSecret.Core
{
    /// <summary>
    /// Expands a template against already-resolved entry field values.
    /// Resolution of {REF:...} and friends happens before this, in the KeePass adapter,
    /// so this stays free of any KeePass dependency and unit-testable offline.
    /// </summary>
    public static class SecretComposer
    {
        public const string FieldTitle = "TITLE";
        public const string FieldUserName = "USERNAME";
        public const string FieldPassword = "PASSWORD";
        public const string FieldUrl = "URL";
        public const string FieldNotes = "NOTES";

        /// <summary>
        /// The per-share custom message. It is a substituted value rather than literal
        /// template text, which keeps the template readable (the template shows {MESSAGE},
        /// not a copy of the message) and means braces the user types in a message are never
        /// re-interpreted as placeholders.
        /// </summary>
        public const string FieldMessage = "MESSAGE";

        public static readonly string DefaultTemplate =
            "{" + FieldUserName + "}\r\n{" + FieldPassword + "}";

        /// <summary>
        /// Substitutes {FIELD} placeholders in a single pass. Substituted values are never
        /// rescanned, so a field value containing literal braces stays literal. Unknown
        /// placeholders are left as written rather than silently dropped.
        /// </summary>
        public static string Compose(string template, IDictionary<string, string> fields)
        {
            if (template == null) throw new ArgumentNullException("template");
            if (fields == null) throw new ArgumentNullException("fields");

            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in fields)
                lookup[pair.Key] = pair.Value ?? string.Empty;

            var sb = new StringBuilder(template.Length);
            int i = 0;

            while (i < template.Length)
            {
                char c = template[i];
                if (c != '{')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                // A '{' only opens a placeholder if its '}' arrives before any further '{'.
                // Otherwise the brace is literal text - without this, a stray '{' in a custom
                // message swallows the next real placeholder and the share silently goes out
                // containing "{PASSWORD}" instead of the password.
                int close = -1;
                for (int j = i + 1; j < template.Length; j++)
                {
                    if (template[j] == '{') break;
                    if (template[j] == '}') { close = j; break; }
                }

                if (close < 0)
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                string name = template.Substring(i + 1, close - i - 1);
                string value;
                if (name.Length > 0 && lookup.TryGetValue(name, out value))
                    sb.Append(value);
                else
                    sb.Append(template, i, close - i + 1);

                i = close + 1;
            }

            return NormalizeNewLines(sb.ToString());
        }

        /// <summary>Collapses mixed CR / LF / CRLF to CRLF so multi-line notes survive the round trip.</summary>
        public static string NormalizeNewLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    sb.Append("\r\n");
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                }
                else if (c == '\n')
                {
                    sb.Append("\r\n");
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
