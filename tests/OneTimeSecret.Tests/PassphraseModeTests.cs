using OneTimeSecret.Core;
using Xunit;

namespace OneTimeSecret.Tests
{
    public class PassphraseModeTests
    {
        [Theory]
        [InlineData(PassphraseMode.None, "none")]
        [InlineData(PassphraseMode.Generate, "generate")]
        [InlineData(PassphraseMode.Manual, "manual")]
        [InlineData(PassphraseMode.Entry, "entry")]
        public void EveryMode_RoundTripsThroughConfig(PassphraseMode mode, string expected)
        {
            string stored = PassphraseModes.ToConfigValue(mode);
            Assert.Equal(expected, stored);
            Assert.Equal(mode, PassphraseModes.Parse(stored, PassphraseMode.None));
        }

        [Theory]
        [InlineData("GENERATE", PassphraseMode.Generate)]
        [InlineData("Entry", PassphraseMode.Entry)]
        public void ParsingIsCaseInsensitive(string stored, PassphraseMode expected)
        {
            Assert.Equal(expected, PassphraseModes.Parse(stored, PassphraseMode.None));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("nonsense")]
        public void UnknownOrMissingValues_FallBack(string stored)
        {
            // A hand-edited config must not silently downgrade a share to no passphrase, so
            // the caller's fallback wins rather than a hardcoded None.
            Assert.Equal(PassphraseMode.Generate, PassphraseModes.Parse(stored, PassphraseMode.Generate));
        }

        [Fact]
        public void EveryMode_HasADistinctLabel()
        {
            var modes = new[] { PassphraseMode.None, PassphraseMode.Generate,
                PassphraseMode.Manual, PassphraseMode.Entry };

            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (PassphraseMode mode in modes)
            {
                string label = PassphraseModes.Describe(mode);
                Assert.False(string.IsNullOrEmpty(label));
                Assert.True(seen.Add(label), "duplicate label: " + label);
            }
        }
    }
}
