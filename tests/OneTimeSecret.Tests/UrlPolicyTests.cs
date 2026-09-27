using System;
using OneTimeSecret.Core;
using Xunit;

namespace OneTimeSecret.Tests
{
    public class UrlPolicyTests
    {
        [Fact]
        public void Https_IsAccepted()
        {
            Uri uri = UrlPolicy.Validate("https://eu.onetimesecret.com", false);
            Assert.Equal("https://eu.onetimesecret.com/", uri.AbsoluteUri);
        }

        [Fact]
        public void TrailingSlashes_AreTrimmedFromPath()
        {
            Uri uri = UrlPolicy.Validate("https://example.com/ots///", false);
            Assert.Equal("https://example.com/ots", uri.AbsoluteUri);
        }

        [Fact]
        public void PathPrefix_IsPreserved()
        {
            Uri uri = UrlPolicy.Validate("https://example.com/ots", false);
            Assert.Equal("https://example.com/ots", uri.AbsoluteUri);
        }

        [Fact]
        public void QueryAndFragment_AreStripped()
        {
            Uri uri = UrlPolicy.Validate("https://example.com/ots?a=1#frag", false);
            Assert.Equal("https://example.com/ots", uri.AbsoluteUri);
        }

        [Fact]
        public void PlainHttp_ToPublicHost_IsRefused()
        {
            var ex = Assert.Throws<UrlPolicyException>(() => UrlPolicy.Validate("http://example.com", false));
            Assert.Contains("plain http", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PlainHttp_ToPublicHost_IsRefusedEvenWithInsecureFlag()
        {
            // The loopback rule is not a general http escape hatch. There is no third case.
            Assert.Throws<UrlPolicyException>(() => UrlPolicy.Validate("http://example.com", true));
        }

        [Theory]
        [InlineData("http://127.0.0.1:3000")]
        [InlineData("http://localhost:3000")]
        [InlineData("http://LOCALHOST:3000")]
        [InlineData("http://[::1]:3000")]
        [InlineData("http://127.0.0.2:3000")]
        public void PlainHttp_ToLoopback_RequiresTheInsecureFlag(string url)
        {
            Assert.Throws<UrlPolicyException>(() => UrlPolicy.Validate(url, false));
            Uri uri = UrlPolicy.Validate(url, true);
            Assert.Equal("http", uri.Scheme);
        }

        [Fact]
        public void Https_ToLoopback_NeedsNoFlag()
        {
            Uri uri = UrlPolicy.Validate("https://127.0.0.1:3000", false);
            Assert.Equal("https://127.0.0.1:3000/", uri.AbsoluteUri);
        }

        [Theory]
        [InlineData("file:///C:/secrets.txt")]
        [InlineData("ftp://example.com")]
        [InlineData("javascript:alert(1)")]
        public void NonHttpSchemes_AreRefused(string url)
        {
            Assert.Throws<UrlPolicyException>(() => UrlPolicy.Validate(url, true));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("example.com")]
        [InlineData("not a url")]
        public void EmptyOrRelativeInput_IsRefused(string url)
        {
            Assert.Throws<UrlPolicyException>(() => UrlPolicy.Validate(url, false));
        }

        [Fact]
        public void InternationalizedHost_IsAccepted()
        {
            Uri uri = UrlPolicy.Validate("https://b\u00fccher.example", false);
            Assert.Equal("https", uri.Scheme);
        }

        [Fact]
        public void HostnameThatMerelyPointsAtLoopback_IsNotTreatedAsLoopback()
        {
            // Decided from the literal host, with no DNS lookup: fail closed.
            Assert.False(UrlPolicy.IsLoopback(new Uri("http://localtest.me")));
        }
    }
}
