namespace Jellystat_API_Core.Extensions
{
    public static class EnumerableExtensions
    {
        /// <summary>
        /// Converts an IEnumerable to a List, or returns an empty List if the source is null.
        /// </summary>
        /// <typeparam name="T">The type of elements in the enumerable.</typeparam>
        /// <param name="source">The source enumerable to convert.</param>
        /// <returns>A List containing the elements from the source, or an empty List if source is null.</returns>
        public static List<T> ToListOrDefault<T>(this IEnumerable<T>? source)
        {
            return source?.ToList() ?? new List<T>();
        }

        public static bool IsNotEmpty<T>(this IEnumerable<T> source)
        {
            return source.Any();
        }
        public static bool IsEmpty<T>(this IEnumerable<T>? source)
        {
            return source == null || !source.Any();
        }

    }
}
