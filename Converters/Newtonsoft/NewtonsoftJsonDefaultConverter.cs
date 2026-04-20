using Jellystat_API_Core.Attributes;
using Newtonsoft.Json;
using System.Reflection;

namespace Jellystat_API_Core.Converters.Newtonsoft
{
    public class NewtonsoftJsonDefaultConverter<T> : JsonConverter<T> where T : struct
    {
        private readonly T _defaultValue;

        public NewtonsoftJsonDefaultConverter(T defaultValue)
        {
            _defaultValue = defaultValue;
        }

        public override T ReadJson(JsonReader reader, Type objectType, T existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            try
            {
                if (reader.TokenType == JsonToken.Null || reader.TokenType == JsonToken.Undefined)
                {
                    return _defaultValue;
                }

                if (typeof(T).IsEnum)
                {
                    if (reader.TokenType == JsonToken.String)
                    {
                        string? enumString = reader.Value?.ToString();
                        if (string.IsNullOrEmpty(enumString))
                        {
                            return _defaultValue;
                        }

                        if (Enum.TryParse<T>(enumString, ignoreCase: true, out var result))
                        {
                            return result;
                        }

                        return _defaultValue;
                    }

                    if (reader.TokenType == JsonToken.Integer)
                    {
                        int enumValue = Convert.ToInt32(reader.Value);
                        if (Enum.IsDefined(typeof(T), enumValue))
                        {
                            return (T)(object)enumValue;
                        }

                        return _defaultValue;
                    }

                    return _defaultValue;
                }

                // For non-enum types, deserialize normally
                var value = serializer.Deserialize<T>(reader);
                return value;
            }
            catch
            {
                return _defaultValue;
            }
        }

        public override void WriteJson(JsonWriter writer, T value, JsonSerializer serializer)
        {
            if (typeof(T).IsEnum)
            {
                writer.WriteValue(value.ToString());
            }
            else
            {
                serializer.Serialize(writer, value);
            }
        }
    }
}
