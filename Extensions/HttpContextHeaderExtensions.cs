namespace Jellystat_API_Core.Extensions
{
    /// <summary>
    /// Extension methods for HttpContext to retrieve validated header values.
    /// </summary>
    public static class HttpContextHeaderExtensions
    {
        /// <summary>
        /// Gets a validated header value that was stored by ValidateHeaderAttribute.
        /// </summary>
        /// <param name="context">The HttpContext.</param>
        /// <param name="headerName">The name of the header.</param>
        /// <returns>The header value if found, otherwise null.</returns>
        public static string? GetValidatedHeader(this HttpContext context, string headerName)
        {
            if (context.Items.TryGetValue($"Header_{headerName}", out var value))
            {
                return value?.ToString();
            }
            return null;
        }

        /// <summary>
        /// Tries to get a validated header value that was stored by ValidateHeaderAttribute.
        /// </summary>
        /// <param name="context">The HttpContext.</param>
        /// <param name="headerName">The name of the header.</param>
        /// <param name="headerValue">The header value if found.</param>
        /// <returns>True if the header was found, otherwise false.</returns>
        public static bool TryGetValidatedHeader(this HttpContext context, string headerName, out string? headerValue)
        {
            headerValue = context.GetValidatedHeader(headerName);
            return headerValue != null;
        }
    }
}
