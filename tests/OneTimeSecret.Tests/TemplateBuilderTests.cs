using System.Collections.Generic;
using OneTimeSecret.Core;
using Xunit;

namespace OneTimeSecret.Tests
{
    public class TemplateBuilderTests
    {
        private static TemplateFields Fields(bool title = false, bool user = false,
            bool password = false, bool url = false, bool notes = false,
            bool message = false, bool messageAfterFields = false)
        {
            return new TemplateFields
            {
                Title = title,
                UserName = user,
                Password = password,
                Url = url,
                Notes = notes,
                Message = message,
                MessageAfterFields = messageAfterFields,
            };
        }

        [Fact]
        public void Default_MatchesTheDocumentedDefaultTemplate()
        {
            Assert.Equal(SecretComposer.DefaultTemplate, TemplateBuilder.Build(TemplateFields.Default()));
        }

        [Fact]
        public void NoFieldsAndNoMessage_ProducesNothing()
        {
            Assert.Equal(string.Empty, TemplateBuilder.Build(Fields()));
        }

        [Fact]
        public void MessageOnly_ProducesJustTheToken()
        {
            Assert.Equal("{MESSAGE}", TemplateBuilder.Build(Fields(message: true)));
        }

        [Fact]
        public void FieldsOnly_HaveNoMessageToken()
        {
            Assert.Equal("{USERNAME}\r\n{PASSWORD}", TemplateBuilder.Build(Fields(user: true, password: true)));
        }

        [Fact]
        public void MessageTokenSitsAboveTheFieldBlockByDefault()
        {
            Assert.Equal(
                "{MESSAGE}\r\n\r\n{USERNAME}\r\n{PASSWORD}",
                TemplateBuilder.Build(Fields(user: true, password: true, message: true)));
        }

        [Fact]
        public void MessageTokenCanSitBelowTheFieldBlock()
        {
            Assert.Equal(
                "{USERNAME}\r\n{PASSWORD}\r\n\r\n{MESSAGE}",
                TemplateBuilder.Build(Fields(user: true, password: true, message: true, messageAfterFields: true)));
        }

        [Theory]
        [InlineData("{MESSAGE}\r\n\r\n{USERNAME}", false)]
        [InlineData("{USERNAME}\r\n\r\n{MESSAGE}", true)]
        [InlineData("{USERNAME}\r\n{PASSWORD}", false)]
        [InlineData("{MESSAGE}", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void MessageOrderingIsReadBackFromAnExistingTemplate(string template, bool expected)
        {
            // This is what lets a layout set in Options survive a regeneration.
            Assert.Equal(expected, TemplateBuilder.MessageFollowsFields(template));
        }

        [Theory]
        [InlineData("{MESSAGE}\r\n{USERNAME}", true)]
        [InlineData("{message}", true)]
        [InlineData("{USERNAME}", false)]
        [InlineData("", false)]
        public void MessageTokenPresenceIsDetected(string template, bool expected)
        {
            Assert.Equal(expected, TemplateBuilder.ContainsMessageToken(template));
        }

        [Fact]
        public void TheMessageTextNeverAppearsInTheTemplate()
        {
            // The whole point of the token: the template shows structure, not a duplicate of
            // what the user just typed into the message box.
            string template = TemplateBuilder.Build(Fields(password: true, message: true));

            Assert.DoesNotContain("Das ist ein Test", template);
            Assert.Contains("{MESSAGE}", template);
        }

        [Fact]
        public void FieldOrderIsFixedRegardlessOfWhichAreChecked()
        {
            string all = TemplateBuilder.Build(Fields(true, true, true, true, true));
            Assert.Equal("{TITLE}\r\n{USERNAME}\r\n{PASSWORD}\r\n{URL}\r\n{NOTES}", all);
        }

        [Theory]
        [InlineData(false, false, false, false, true, "{NOTES}")]
        [InlineData(true, false, false, false, false, "{TITLE}")]
        [InlineData(true, false, true, false, false, "{TITLE}\r\n{PASSWORD}")]
        [InlineData(false, true, false, true, false, "{USERNAME}\r\n{URL}")]
        [InlineData(true, true, true, false, false, "{TITLE}\r\n{USERNAME}\r\n{PASSWORD}")]
        public void SelectedFieldsAppearInSpecOrder(
            bool title, bool user, bool password, bool url, bool notes, string expected)
        {
            Assert.Equal(expected, TemplateBuilder.Build(Fields(title, user, password, url, notes)));
        }

        [Fact]
        public void EverySubsetOfFieldsProducesExactlyItsOwnPlaceholders()
        {
            // M3 acceptance asks for every field-inclusion combination; there are only 32.
            string[] names = { "{TITLE}", "{USERNAME}", "{PASSWORD}", "{URL}", "{NOTES}" };

            for (int mask = 0; mask < 32; mask++)
            {
                var fields = Fields(
                    (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, (mask & 16) != 0);

                var expected = new List<string>();
                for (int bit = 0; bit < 5; bit++)
                    if ((mask & (1 << bit)) != 0) expected.Add(names[bit]);

                Assert.Equal(string.Join("\r\n", expected.ToArray()), TemplateBuilder.Build(fields));
                Assert.Equal(fields.Any, mask != 0);
            }
        }

        [Fact]
        public void BracesInTheMessageStayLiteral()
        {
            // Because the message is a substituted value rather than template text, and
            // SecretComposer never rescans what it substitutes. Typing "{PASSWORD}" in a
            // message prints it, it does not leak the password a second time.
            string template = TemplateBuilder.Build(Fields(user: true, message: true));
            Assert.Equal("{MESSAGE}\r\n\r\n{USERNAME}", template);

            var values = new Dictionary<string, string>
            {
                { SecretComposer.FieldMessage, "your {PASSWORD} is below" },
                { SecretComposer.FieldUserName, "alice" },
                { SecretComposer.FieldPassword, "hunter2" },
            };
            Assert.Equal("your {PASSWORD} is below\r\n\r\nalice", SecretComposer.Compose(template, values));
        }

        [Fact]
        public void AnUnmatchedBraceInTheMessageCannotBreakTheFieldsBelowIt()
        {
            string template = TemplateBuilder.Build(Fields(password: true, message: true));

            var values = new Dictionary<string, string>
            {
                { SecretComposer.FieldMessage, "costs {100 and up" },
                { SecretComposer.FieldPassword, "pw" },
            };
            Assert.Equal("costs {100 and up\r\n\r\npw", SecretComposer.Compose(template, values));
        }
    }
}
