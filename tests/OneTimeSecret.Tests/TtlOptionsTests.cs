using System.Linq;
using OneTimeSecret.Core;
using Xunit;

namespace OneTimeSecret.Tests
{
    public class TtlOptionsTests
    {
        [Fact]
        public void Presets_CoverTheValuesTheSpecRequires()
        {
            int[] seconds = TtlOptions.Presets().Select(o => o.Seconds).ToArray();
            Assert.Equal(new[] { 300, 3600, 86400, 259200, 604800 }, seconds);
        }

        [Fact]
        public void Presets_DefaultIsOneDay()
        {
            Assert.Equal(86400, TtlOptions.DefaultSeconds);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-86400)]
        public void NonPositiveStoredValues_FallBackToTheDefault(long stored)
        {
            // An unset or hand-corrupted config value must not be sent to the server.
            Assert.Equal(TtlOptions.DefaultSeconds, TtlOptions.Sanitize(stored));
        }

        [Fact]
        public void OversizedStoredValue_IsClampedRatherThanOverflowing()
        {
            Assert.Equal(int.MaxValue, TtlOptions.Sanitize(long.MaxValue));
        }

        [Fact]
        public void ValidStoredValue_PassesThrough()
        {
            Assert.Equal(3600, TtlOptions.Sanitize(3600));
        }

        [Theory]
        [InlineData(300, "5 minutes")]
        [InlineData(86400, "1 day")]
        [InlineData(604800, "7 days")]
        [InlineData(172800, "2 days")]
        [InlineData(7200, "2 hours")]
        [InlineData(900, "15 minutes")]
        [InlineData(45, "45 seconds")]
        public void Describe_PrefersPresetLabelsThenTheLargestWholeUnit(int seconds, string expected)
        {
            Assert.Equal(expected, TtlOptions.Describe(seconds));
        }
    }
}
