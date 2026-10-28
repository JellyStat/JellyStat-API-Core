using System.Security.Cryptography;
using System.Text;

namespace Jellystat_API_Core.Helpers
{
    public static class ApiKeyHelper
    {
        public const string KeyPrefixLiteral = "js_";
        private const int PrefixDisplayLength = 8;

        /// <summary>Generates a new random key (256 bits, URL-safe) with the js_ prefix.</summary>
        public static string Generate()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(32);
            string token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return KeyPrefixLiteral + token;
        }

        public static string DisplayPrefix(string key) => key.Substring(0, Math.Min(PrefixDisplayLength, key.Length));

        /// <summary>SHA-256 is sufficient because keys are high-entropy random values, not passwords.</summary>
        public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }
}
