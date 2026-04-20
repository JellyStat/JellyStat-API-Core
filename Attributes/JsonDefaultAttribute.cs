using System;

namespace Jellystat_API_Core.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class JsonDefaultAttribute : Attribute
    {
        public object DefaultValue { get; }

        public JsonDefaultAttribute(object defaultValue)
        {
            DefaultValue = defaultValue;
        }
    }
}
