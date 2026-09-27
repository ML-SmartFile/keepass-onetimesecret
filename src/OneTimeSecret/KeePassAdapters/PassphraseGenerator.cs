using System;
using KeePass.Plugins;
using KeePassLib.Cryptography.PasswordGenerator;
using KeePassLib.Security;

namespace OneTimeSecret.KeePassAdapters
{
    /// <summary>
    /// Generates the passphrase that gates a share, using KeePass's own generator so it
    /// inherits KeePass's CSPRNG rather than a hand-rolled one.
    /// </summary>
    internal static class PassphraseGenerator
    {
        /// <summary>
        /// Long enough to be meaningful, short enough to retype from a phone. Look-alike
        /// characters are excluded because this gets read off one screen and typed into
        /// another, and a mistyped passphrase looks identical to a burned secret.
        /// </summary>
        public const uint Length = 20;

        public static string Generate(IPluginHost host)
        {
            var profile = new PwProfile
            {
                GeneratorType = PasswordGeneratorType.CharSet,
                Length = Length,
                CharSet = new PwCharSet(PwCharSet.UpperCase + PwCharSet.LowerCase + PwCharSet.Digits),
                ExcludeLookAlike = true,
                NoRepeatingCharacters = false,
            };

            ProtectedString generated;
            PwgError error = PwGenerator.Generate(
                out generated, profile, null,
                (host != null) ? host.PwGeneratorPool : null);

            if (error != PwgError.Success || generated == null)
                throw new InvalidOperationException(
                    "KeePass could not generate a passphrase (" + error + ").");

            return generated.ReadString();
        }
    }
}
