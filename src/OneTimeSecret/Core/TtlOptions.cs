using System;
using System.Collections.Generic;
using System.Globalization;

namespace OneTimeSecret.Core
{
    /// <summary>The lifetimes offered in the UI, per SPEC.md section 5.</summary>
    public sealed class TtlOption
    {
        public TtlOption(string label, int seconds)
        {
            Label = label;
            Seconds = seconds;
        }

        public string Label { get; private set; }
        public int Seconds { get; private set; }

        public override string ToString() { return Label; }
    }

    public static class TtlOptions
    {
        public const int DefaultSeconds = 86400;

        public static IList<TtlOption> Presets()
        {
            return new List<TtlOption>
            {
                new TtlOption("5 minutes", 300),
                new TtlOption("1 hour", 3600),
                new TtlOption("1 day", 86400),
                new TtlOption("3 days", 259200),
                new TtlOption("7 days", 604800),
            };
        }

        /// <summary>
        /// Clamps a persisted value into something sendable. A stored zero or negative
        /// value means "never configured" or a corrupted config; fall back to the default
        /// rather than asking the server to accept nonsense.
        /// </summary>
        public static int Sanitize(long seconds)
        {
            if (seconds <= 0) return DefaultSeconds;
            if (seconds > int.MaxValue) return int.MaxValue;
            return (int)seconds;
        }

        /// <summary>Human-readable form for an arbitrary second count, including custom values.</summary>
        public static string Describe(int seconds)
        {
            foreach (TtlOption preset in Presets())
                if (preset.Seconds == seconds) return preset.Label;

            if (seconds % 86400 == 0)
                return (seconds / 86400).ToString(CultureInfo.InvariantCulture) + " days";
            if (seconds % 3600 == 0)
                return (seconds / 3600).ToString(CultureInfo.InvariantCulture) + " hours";
            if (seconds % 60 == 0)
                return (seconds / 60).ToString(CultureInfo.InvariantCulture) + " minutes";

            return seconds.ToString(CultureInfo.InvariantCulture) + " seconds";
        }
    }
}
