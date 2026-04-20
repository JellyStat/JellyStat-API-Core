using Newtonsoft.Json;

namespace Jellystat_API_Core.Extensions
{
    public static class HttpContentExtensions
    {


        /// <summary>
        /// Reads the HTTP content as a string and deserializes it to the specified type.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to.</typeparam>
        /// <param name="content">The HTTP content to read and deserialize.</param>
        /// <returns>The deserialized object of type T.</returns>
        /// <exception cref="JsonException">Thrown when the JSON cannot be deserialized to the specified type.</exception>
        public static async Task<T> ReadAsJsonAsync<T>(this HttpContent content)
        {
            var json = await content.ReadAsStringAsync();

            try
            {
                var result = JsonConvert.DeserializeObject<T>(json, Global.jsonSettings);

                if (result == null)
                {
                    throw new JsonException($"Failed to deserialize JSON to type {typeof(T).Name}. The result was null.");
                }

                return result;
            }
            catch (JsonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new JsonException($"Failed to deserialize JSON to type {typeof(T).Name}.", ex);
            }
        }
    }
}
