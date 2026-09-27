using System;
using System.Text;

namespace OneTimeSecret.Ots
{
    /// <summary>
    /// The OneTimeSecret account credentials: the account email and the API token.
    /// Read from a user-chosen database entry at call time - never persisted by this plugin.
    /// </summary>
    public sealed class OtsCredentials
    {
        public static readonly OtsCredentials Anonymous = new OtsCredentials(null, null);

        public OtsCredentials(string userName, string token)
        {
            UserName = userName;
            Token = token;
        }

        public string UserName { get; private set; }
        public string Token { get; private set; }

        public bool IsAnonymous
        {
            get { return string.IsNullOrEmpty(UserName) || string.IsNullOrEmpty(Token); }
        }
    }

    public static class BasicAuth
    {
        /// <summary>
        /// Builds an HTTP Basic value, or null for anonymous. The returned string is a
        /// credential: .NET strings cannot be scrubbed, so the intermediate byte buffer is
        /// zeroed and the limitation is documented rather than papered over.
        /// </summary>
        public static string Header(OtsCredentials credentials)
        {
            if (credentials == null || credentials.IsAnonymous) return null;

            byte[] raw = null;
            try
            {
                raw = Encoding.UTF8.GetBytes(credentials.UserName + ":" + credentials.Token);
                return "Basic " + Convert.ToBase64String(raw);
            }
            finally
            {
                if (raw != null) Array.Clear(raw, 0, raw.Length);
            }
        }
    }
}
