namespace Jellystat_API_Core.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class JsonIgnoreOnWriteAttribute : Attribute
    {
    }
}
