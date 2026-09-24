using System;
using System.Text;
using OneTimeSecret.Ots;
using Xunit;

namespace OneTimeSecret.Tests
{
    public class BasicAuthTests
    {
        [Fact]
        public void Anonymous_ProducesNoHeader()
        {
            Assert.Null(BasicAuth.Header(OtsCredentials.Anonymous));
            Assert.Null(BasicAuth.Header(null));
        }

        [Theory]
        [InlineData(null, "token")]
        [InlineData("", "token")]
        [InlineData("user@example.com", null)]
        [InlineData("user@example.com", "")]
        public void PartialCredentials_CountAsAnonymous(string user, string token)
        {
            var credentials = new OtsCredentials(user, token);
            Assert.True(credentials.IsAnonymous);
            Assert.Null(BasicAuth.Header(credentials));
        }

        [Fact]
        public void FullCredentials_ProduceBase64OfUserColonToken()
        {
            string header = BasicAuth.Header(new OtsCredentials("user@example.com", "tok3n"));

            Assert.StartsWith("Basic ", header);
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Substring(6)));
            Assert.Equal("user@example.com:tok3n", decoded);
        }

        [Fact]
        public void NonAsciiCredentials_AreEncodedAsUtf8()
        {
            string header = BasicAuth.Header(new OtsCredentials("jürgen@example.com", "geheßim"));

            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Substring(6)));
            Assert.Equal("jürgen@example.com:geheßim", decoded);
        }

        [Fact]
        public void ATokenContainingAColon_IsPreserved()
        {
            // Only the first colon separates the pair; the rest belongs to the token.
            string header = BasicAuth.Header(new OtsCredentials("user", "a:b:c"));

            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Substring(6)));
            Assert.Equal("user:a:b:c", decoded);
        }
    }
}
