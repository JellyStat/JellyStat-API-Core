using Gridify;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellystat_API_Core.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class DefaultGridifyOrderAttribute : ActionFilterAttribute
    {
        private readonly string _orderBy;

        public DefaultGridifyOrderAttribute(string propertyName, bool descending = false)
        {
            _orderBy = descending ? $"{propertyName} desc" : propertyName;
        }

        public DefaultGridifyOrderAttribute(string[] propertyNames, bool[] descending)
        {
            if (propertyNames == null || propertyNames.Length == 0)
                throw new ArgumentException("At least one property name must be provided.", nameof(propertyNames));

            if (descending != null && descending.Length != propertyNames.Length)
                throw new ArgumentException("The descending array must have the same length as propertyNames.", nameof(descending));

            var orderParts = new string[propertyNames.Length];
            for (int i = 0; i < propertyNames.Length; i++)
            {
                bool isDescending = descending != null && descending[i];
                orderParts[i] = isDescending ? $"{propertyNames[i]} desc" : propertyNames[i];
            }

            _orderBy = string.Join(", ", orderParts);
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            foreach (var argument in context.ActionArguments.Values)
            {
                if (argument is GridifyQuery query && string.IsNullOrWhiteSpace(query.OrderBy))
                {
                    query.OrderBy = _orderBy;
                }
            }

            base.OnActionExecuting(context);
        }
    }
}
