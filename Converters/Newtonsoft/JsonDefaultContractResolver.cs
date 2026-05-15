using Jellystat_API_Core.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace Jellystat_API_Core.Converters.Newtonsoft
{
    public class JsonDefaultContractResolver : CamelCasePropertyNamesContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            var ignoreOnWriteAttribute = member.GetCustomAttribute<JsonIgnoreOnWriteAttribute>();
            if (ignoreOnWriteAttribute != null)
            {
                property.ShouldSerialize = _ => false;
            }

            var ignoreOnReadAttribute = member.GetCustomAttribute<JsonIgnoreOnReadAttribute>();
            if (ignoreOnReadAttribute != null)
            {
                property.ShouldDeserialize = _ => false;
            }

            var defaultAttribute = member.GetCustomAttribute<JsonDefaultAttribute>();

            if (defaultAttribute != null && property.PropertyType != null && property.PropertyType.IsValueType)
            {
                var converterType = typeof(NewtonsoftJsonDefaultConverter<>).MakeGenericType(property.PropertyType);
                var converter = Activator.CreateInstance(converterType, defaultAttribute.DefaultValue);

                if (converter != null)
                {
                    property.Converter = (JsonConverter)converter;
                }
            }

            return property;
        }
    }
}
