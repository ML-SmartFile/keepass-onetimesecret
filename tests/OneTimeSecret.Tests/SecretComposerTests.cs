using System.Collections.Generic;
using OneTimeSecret.Core;
using Xunit;

namespace OneTimeSecret.Tests
{
    public class SecretComposerTests
    {
        private static Dictionary<string, string> Fields(
            string user = "alice", string password = "Simple123!",
            string title = "Test Simple", string url = "", string notes = "")
        {
            return new Dictionary<string, string>
            {
                { SecretComposer.FieldTitle, title },
                { SecretComposer.FieldUserName, user },
                { SecretComposer.FieldPassword, password },
                { SecretComposer.FieldUrl, url },
                { SecretComposer.FieldNotes, notes },
            };
        }

        [Fact]
        public void DefaultTemplate_ExpandsUserNameAndPassword()
        {
            string result = SecretComposer.Compose(SecretComposer.DefaultTemplate, Fields());
            Assert.Equal("alice\r\nSimple123!", result);
        }

        [Fact]
        public void EmptyField_ExpandsToNothing()
        {
            string result = SecretComposer.Compose("{USERNAME}|{URL}|{PASSWORD}", Fields(url: ""));
            Assert.Equal("alice||Simple123!", result);
        }

        [Fact]
        public void PlaceholderNames_AreCaseInsensitive()
        {
            string result = SecretComposer.Compose("{username}/{Password}", Fields());
            Assert.Equal("alice/Simple123!", result);
        }

        [Fact]
        public void UnknownPlaceholder_IsLeftLiteral()
        {
            string result = SecretComposer.Compose("{USERNAME} {NOPE}", Fields());
            Assert.Equal("alice {NOPE}", result);
        }

        [Fact]
        public void SubstitutedValue_IsNotRescanned()
        {
            // A password that happens to look like a placeholder must survive verbatim.
            string result = SecretComposer.Compose("{PASSWORD}", Fields(password: "{TITLE}"));
            Assert.Equal("{TITLE}", result);
        }

        [Fact]
        public void UnmatchedBrace_IsLeftLiteral()
        {
            string result = SecretComposer.Compose("{USERNAME} {UNCLOSED", Fields());
            Assert.Equal("alice {UNCLOSED", result);
        }

        [Fact]
        public void AStrayBraceDoesNotSwallowTheNextPlaceholder()
        {
            // Regression: the scanner used to search for the next '}' from any '{', so a stray
            // brace consumed everything up to the close of a *later* placeholder - the share
            // then went out containing the literal text "{PASSWORD}" instead of the password.
            string result = SecretComposer.Compose("costs {100 and up\r\n{PASSWORD}", Fields(password: "hunter2"));
            Assert.Equal("costs {100 and up\r\nhunter2", result);
        }

        [Fact]
        public void SeveralStrayBraces_StillLeaveLaterPlaceholdersIntact()
        {
            string result = SecretComposer.Compose("{ { {USERNAME} }", Fields());
            Assert.Equal("{ { alice }", result);
        }

        [Fact]
        public void EmptyBraces_AreLeftLiteral()
        {
            string result = SecretComposer.Compose("a{}b", Fields());
            Assert.Equal("a{}b", result);
        }

        [Theory]
        [InlineData("line1\nline2", "line1\r\nline2")]
        [InlineData("line1\r\nline2", "line1\r\nline2")]
        [InlineData("line1\rline2", "line1\r\nline2")]
        [InlineData("a\r\n\r\nb", "a\r\n\r\nb")]
        [InlineData("a\n\nb", "a\r\n\r\nb")]
        public void MultilineNotes_AreNormalizedToCrLf(string notes, string expected)
        {
            string result = SecretComposer.Compose("{NOTES}", Fields(notes: notes));
            Assert.Equal(expected, result);
        }

        [Fact]
        public void UmlautsAndNonAscii_SurviveUnchanged()
        {
            string result = SecretComposer.Compose("{PASSWORD}", Fields(password: "Pässwörd-Ümlaut-ß!"));
            Assert.Equal("Pässwörd-Ümlaut-ß!", result);
        }

        [Fact]
        public void LoneLineFeeds_AreRaisedToCrLf()
        {
            // The plugin config is XML, and XML parsing collapses CRLF to a bare LF. A
            // WinForms multi-line TextBox only breaks on CRLF, so a template round-tripped
            // through config renders as one run-on line unless it is normalized on read.
            Assert.Equal("{USERNAME}\r\n{PASSWORD}",
                SecretComposer.NormalizeNewLines("{USERNAME}\n{PASSWORD}"));

            Assert.Equal("{USERNAME}\r\n\r\n{PASSWORD}",
                SecretComposer.NormalizeNewLines("{USERNAME}\n\n{PASSWORD}"));
        }

        [Fact]
        public void AlreadyNormalizedText_IsUnchanged()
        {
            Assert.Equal("a\r\nb", SecretComposer.NormalizeNewLines("a\r\nb"));
        }

        [Fact]
        public void MissingFieldInDictionary_LeavesPlaceholderLiteral()
        {
            var sparse = new Dictionary<string, string> { { SecretComposer.FieldUserName, "alice" } };
            string result = SecretComposer.Compose("{USERNAME}\r\n{PASSWORD}", sparse);
            Assert.Equal("alice\r\n{PASSWORD}", result);
        }
    }
}
