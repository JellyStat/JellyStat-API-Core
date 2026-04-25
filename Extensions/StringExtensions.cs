using System.Text.RegularExpressions;

namespace Jellystat_API_Core.Extensions
{
    public static class StringExtensions
    {
        /// <summary>
        /// Converts an enum value to a formatted string.
        /// Example: non_stock => "Non stock", IS_ADMIN => "IS ADMIN"
        /// </summary>
        public static string ToDisplayString(this String value)
        {
            // Convert enum to string, replace underscores with spaces, and capitalize first letter
            var str = value.ToString().Replace("_", " ");
            return Regex.Replace(str, @"(^\w)|(\s\w)", m => m.Value.ToUpper());
        }
    }
}
