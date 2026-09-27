using System;
using System.Collections.Generic;
using System.Text;

namespace OneTimeSecret.Core
{
    /// <summary>Which entry fields the share should include, in the order they are emitted.</summary>
    public sealed class TemplateFields
    {
        public bool Title { get; set; }
        public bool UserName { get; set; }
        public bool Password { get; set; }
        public bool Url { get; set; }
        public bool Notes { get; set; }

        /// <summary>Whether the per-share custom message is included at all.</summary>
        public bool Message { get; set; }

        /// <summary>
        /// Where {MESSAGE} sits relative to the field block. Only above-or-below is
        /// preserved: the template is regenerated wholesale on every checkbox change, so
        /// an arbitrary hand-placed position cannot survive that and is not pretended to.
        /// </summary>
        public bool MessageAfterFields { get; set; }

        public static TemplateFields Default()
        {
            return new TemplateFields { UserName = true, Password = true };
        }

        public bool Any
        {
            get { return Title || UserName || Password || Url || Notes || Message; }
        }
    }

    /// <summary>
    /// Turns the field checkboxes into a template.
    ///
    /// The message appears as the token <c>{MESSAGE}</c>, never as its text: the template
    /// stays readable instead of duplicating what the user just typed, and because the
    /// message is then a substituted value rather than template text, braces inside it are
    /// never re-interpreted as placeholders.
    /// </summary>
    public static class TemplateBuilder
    {
        public static string Build(TemplateFields fields)
        {
            if (fields == null) throw new ArgumentNullException("fields");

            var placeholders = new List<string>(5);
            if (fields.Title) placeholders.Add("{" + SecretComposer.FieldTitle + "}");
            if (fields.UserName) placeholders.Add("{" + SecretComposer.FieldUserName + "}");
            if (fields.Password) placeholders.Add("{" + SecretComposer.FieldPassword + "}");
            if (fields.Url) placeholders.Add("{" + SecretComposer.FieldUrl + "}");
            if (fields.Notes) placeholders.Add("{" + SecretComposer.FieldNotes + "}");

            string fieldBlock = string.Join("\r\n", placeholders.ToArray());

            if (!fields.Message) return fieldBlock;

            string messageToken = "{" + SecretComposer.FieldMessage + "}";
            if (fieldBlock.Length == 0) return messageToken;

            return fields.MessageAfterFields
                ? fieldBlock + "\r\n\r\n" + messageToken
                : messageToken + "\r\n\r\n" + fieldBlock;
        }

        /// <summary>
        /// Reads back from an existing template whether the message sat after the fields, so
        /// a default template written in Options keeps its ordering across a regeneration.
        /// </summary>
        public static bool MessageFollowsFields(string template)
        {
            if (string.IsNullOrEmpty(template)) return false;

            int message = template.IndexOf("{" + SecretComposer.FieldMessage + "}",
                StringComparison.OrdinalIgnoreCase);
            if (message < 0) return false;

            foreach (string name in new[] { SecretComposer.FieldTitle, SecretComposer.FieldUserName,
                SecretComposer.FieldPassword, SecretComposer.FieldUrl, SecretComposer.FieldNotes })
            {
                int field = template.IndexOf("{" + name + "}", StringComparison.OrdinalIgnoreCase);
                if (field >= 0 && field < message) return true;
            }

            return false;
        }

        public static bool ContainsMessageToken(string template)
        {
            return !string.IsNullOrEmpty(template)
                && template.IndexOf("{" + SecretComposer.FieldMessage + "}",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
