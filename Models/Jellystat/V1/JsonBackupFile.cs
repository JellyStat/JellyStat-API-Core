using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellystat_API_Core.Models.Jellystat.V1
{
    public class JsonBackupFile
    {
        public List<jf_playback_activity> jf_playback_activity { get; set; } = new List<jf_playback_activity>();

        private static readonly JsonSerializerOptions DeserializeOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        };

        public static JsonBackupFile FromArray(JsonElement[] arrayElements)
        {
            var backup = new JsonBackupFile();

            foreach (var element in arrayElements)
            {
                foreach (var property in element.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "jf_playback_activity":
                            backup.jf_playback_activity = DeserializePlaybackActivity(property.Value);
                            break;
                    }
                }
            }

            return backup;
        }

        //Does this because MediaStreams is sometimes an empty object instead of an empty array, which causes deserialization to fail. This method ensures that MediaStreams is always treated as an array.
        private static List<jf_playback_activity> DeserializePlaybackActivity(JsonElement activityArray)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartArray();

                foreach (var activity in activityArray.EnumerateArray())
                {
                    if (activity.ValueKind != JsonValueKind.Object)
                    {
                        activity.WriteTo(writer);
                        continue;
                    }

                    writer.WriteStartObject();

                    foreach (var property in activity.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);

                        if (property.NameEquals("MediaStreams") && IsEmptyObject(property.Value))
                        {
                            writer.WriteStartArray();
                            writer.WriteEndArray();
                            continue;
                        }

                        property.Value.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            return JsonSerializer.Deserialize<List<jf_playback_activity>>(stream.ToArray(), DeserializeOptions) ?? [];
        }

        private static bool IsEmptyObject(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            using var enumerator = element.EnumerateObject();
            return !enumerator.MoveNext();
        }
    }

}

