namespace Jellystat_API_Core.Extensions
{
    public static class CommonDataTypeExtensions
    {
        public static long ToTicks(this long source)
        {
            return source * 10000000;
        }
    }
}
