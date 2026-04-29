using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellystat_API_Core.Converters
{
    public class PostgresDateTimeConverter : JsonConverter<DateTime?>
    {
        private static readonly string[] _formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss",           // PostgreSQL timestamp format
            "yyyy-MM-dd HH:mm:ss.ffffff",    // PostgreSQL timestamp with microseconds
            "yyyy-MM-ddTHH:mm:ss",           // ISO 8601
            "yyyy-MM-ddTHH:mm:ss.ffffff",    // ISO 8601 with microseconds
            "yyyy-MM-ddTHH:mm:ssZ",          // ISO 8601 with UTC
            "yyyy-MM-ddTHH:mm:ss.ffffffZ"    // ISO 8601 with microseconds and UTC
        };

        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                string? dateString = reader.GetString();
                if (string.IsNullOrEmpty(dateString))
                {
                    return null;
                }

                // Try parsing with multiple formats
                if (DateTime.TryParseExact(dateString, _formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
                {
                    return result;
                }

                // Fallback to default parsing
                if (DateTime.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                {
                    return result;
                }

                if (dateString == "-infinity")
                {
                    return DateTime.MinValue;
                }

                throw new JsonException($"Unable to parse '{dateString}' as DateTime.");
            }

            throw new JsonException($"Unexpected token type: {reader.TokenType}");
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteStringValue(value.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
